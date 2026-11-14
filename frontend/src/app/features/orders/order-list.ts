import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { catchError, debounceTime, EMPTY, Subject, switchMap, tap } from 'rxjs';
import { ContactsApi, OrdersApi } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { errorMessage } from '../../core/errors';
import {
  ContactOption,
  CURRENCY,
  orderNumber,
  OrderStats,
  OrderSummary,
  PAYMENT_STATUSES,
  paymentLabel,
  PaymentStatus,
  Permissions,
} from '../../core/models';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/dialogs';
import { PaymentDialog, PaymentDialogData } from './payment';

@Component({
  selector: 'app-order-list',
  imports: [
    DatePipe,
    DecimalPipe,
    ReactiveFormsModule,
    RouterLink,
    MatTableModule,
    MatPaginatorModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatDialogModule,
    MatSnackBarModule,
    MatProgressBarModule,
  ],
  templateUrl: './order-list.html',
  styles: `
    .summary {
      margin: -8px 0 16px;
    }

    .filters {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 200px), 1fr));
      gap: 12px;
      margin-bottom: 16px;
    }

    .search {
      grid-column: span 2;
    }

    .amount {
      white-space: nowrap;
    }

    .payment {
      display: inline-flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 4px 8px;
    }

    button.status {
      border: 0;
      cursor: pointer;

      &:hover,
      &:focus-visible {
        outline: 2px solid var(--mat-sys-primary);
        outline-offset: 1px;
      }
    }

    @media (max-width: 599.98px) {
      .search {
        grid-column: 1 / -1;
      }
    }
  `,
})
export class OrderList implements OnInit {
  private readonly api = inject(OrdersApi);
  private readonly contactsApi = inject(ContactsApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly auth = inject(AuthService);

  protected readonly currency = CURRENCY;
  protected readonly number = orderNumber;
  protected readonly paymentLabel = paymentLabel;
  protected readonly statuses = PAYMENT_STATUSES;
  protected readonly columns = ['order', 'customer', 'total', 'payment', 'actions'];
  protected readonly filters = inject(FormBuilder).nonNullable.group({
    search: [''],
    paymentStatus: [null as PaymentStatus | null],
    customerId: [null as number | null],
  });

  protected readonly orders = signal<OrderSummary[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(0);
  protected readonly pageSize = signal(20);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly stats = signal<OrderStats | null>(null);
  protected readonly customers = signal<ContactOption[]>([]);
  protected readonly canWrite = computed(() => this.auth.can(Permissions.ordersWrite));

  private readonly reload$ = new Subject<void>();

  constructor() {
    this.reload$
      .pipe(
        tap(() => this.loading.set(true)),
        switchMap(() =>
          this.api
            .list({ ...this.filters.getRawValue(), page: this.page() + 1, pageSize: this.pageSize() })
            .pipe(
              catchError((error) => {
                this.error.set(errorMessage(error));
                this.loading.set(false);
                return EMPTY;
              }),
            ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        this.error.set(null);
        this.orders.set(result.items);
        this.total.set(result.total);
        this.loading.set(false);
      });

    this.filters.valueChanges.pipe(debounceTime(300), takeUntilDestroyed()).subscribe(() => {
      this.page.set(0);
      this.reload$.next();
    });
  }

  ngOnInit(): void {
    this.reload();
    this.contactsApi.customerOptions().subscribe((customers) => this.customers.set(customers));
  }

  protected statusClass(status: PaymentStatus): string {
    return status === 'Paid' ? 'active' : status === 'PartiallyPaid' ? 'low' : 'warn';
  }

  protected onPage(event: PageEvent): void {
    this.page.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.reload$.next();
  }

  protected editPayment(order: OrderSummary): void {
    const data: PaymentDialogData = {
      orderId: order.id,
      number: orderNumber(order.id),
      total: order.total,
      paymentStatus: order.paymentStatus,
      amountPaid: order.amountPaid,
    };
    this.dialog
      .open<PaymentDialog, PaymentDialogData, boolean>(PaymentDialog, { data, width: '440px' })
      .afterClosed()
      .subscribe((saved) => {
        if (!saved) return;
        this.notify(`Payment for ${data.number} was saved.`);
        this.reload();
      });
  }

  protected delete(order: OrderSummary): void {
    const data: ConfirmDialogData = {
      title: 'Delete order?',
      message: `${orderNumber(order.id)} will be permanently deleted and its articles put back in stock. This cannot be undone.`,
      confirmLabel: 'Delete',
    };
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data })
      .afterClosed()
      .subscribe((confirmed) => {
        if (!confirmed) return;
        this.api.delete(order.id).subscribe({
          next: () => {
            this.notify(`${orderNumber(order.id)} was deleted.`);
            if (this.orders().length === 1 && this.page() > 0) this.page.update((p) => p - 1);
            this.reload();
          },
          error: (error) => this.notify(errorMessage(error)),
        });
      });
  }

  /** Reloads the page of orders and the amount still due, which every change can affect. */
  private reload(): void {
    this.reload$.next();
    this.api.stats().subscribe((stats) => this.stats.set(stats));
  }

  private notify(message: string): void {
    this.snackBar.open(message, undefined, { duration: 5000 });
  }
}
