import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormArray, FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, filter, map, of, startWith, switchMap } from 'rxjs';
import { ArticlesApi, ContactsApi, OrdersApi } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { applyServerErrors, errorMessage } from '../../core/errors';
import {
  ArticleOption,
  ContactOption,
  CURRENCY,
  Order,
  orderNumber,
  Permissions,
  roundMoney,
  SaveOrderRequest,
  VAT_RATE,
} from '../../core/models';
import { PaymentFields, paymentGroup, paymentRequest } from './payment';

const PRICE = [Validators.required, Validators.min(0), Validators.max(1_000_000_000), Validators.pattern(/^\d+(\.\d{1,3})?$/)];
const QUANTITY = [Validators.required, Validators.min(1), Validators.max(1_000_000), Validators.pattern(/^\d+$/)];

type LineGroup = FormGroup<{
  articleId: FormControl<number>;
  reference: FormControl<string>;
  name: FormControl<string>;
  quantity: FormControl<number>;
  unitPrice: FormControl<number>;
}>;

/** Today in the browser's time zone, as the API's yyyy-mm-dd date. */
function today(): string {
  const now = new Date();
  return new Date(now.getTime() - now.getTimezoneOffset() * 60_000).toISOString().slice(0, 10);
}

@Component({
  selector: 'app-order-form',
  imports: [
    DatePipe,
    DecimalPipe,
    ReactiveFormsModule,
    RouterLink,
    MatAutocompleteModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatSnackBarModule,
    PaymentFields,
  ],
  templateUrl: './order-form.html',
  styleUrl: './order-form.scss',
})
export class OrderForm implements OnInit {
  private readonly api = inject(OrdersApi);
  private readonly articlesApi = inject(ArticlesApi);
  private readonly contactsApi = inject(ContactsApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  /** Route parameter; absent when creating. */
  readonly id = input<string>();

  protected readonly currency = CURRENCY;
  protected readonly canWrite = computed(() => this.auth.can(Permissions.ordersWrite));
  protected readonly title = computed(() => {
    const id = this.id();
    if (!id) return 'New order';
    return `${this.canWrite() ? 'Edit ' : ''}${orderNumber(Number(id))}`;
  });

  protected readonly lines = new FormArray<LineGroup>([], Validators.required);
  protected readonly payment = paymentGroup(() => this.totals().total);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    orderDate: [today(), Validators.required],
    customerId: [null as number | null],
    notes: ['', Validators.maxLength(1000)],
    lines: this.lines,
    payment: this.payment,
  });

  protected readonly order = signal<Order | null>(null);
  protected readonly customers = signal<ContactOption[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly loadFailed = signal(false);

  /** Stock on hand for articles picked in this visit, so a line can warn before the API refuses it. */
  private readonly inStock = signal(new Map<number, number>());

  protected readonly articleSearch = new FormControl('', { nonNullable: true });
  protected readonly articleOptions = toSignal(
    this.articleSearch.valueChanges.pipe(
      startWith(''),
      filter((value) => typeof value === 'string'),
      debounceTime(250),
      distinctUntilChanged(),
      switchMap((search) => this.articlesApi.options(search.trim()).pipe(catchError(() => of([])))),
    ),
    { initialValue: [] as ArticleOption[] },
  );

  private readonly lineValues = toSignal(this.lines.valueChanges.pipe(map(() => this.lines.getRawValue())), {
    initialValue: [],
  });

  protected readonly totals = computed(() => {
    const rate = this.order()?.vatRate ?? VAT_RATE;
    const subtotal = roundMoney(
      this.lineValues().reduce((sum, line) => sum + (Number(line.quantity) || 0) * roundMoney(Number(line.unitPrice) || 0), 0),
    );
    const vatAmount = roundMoney(subtotal * rate);
    return { rate, subtotal, vatAmount, total: roundMoney(subtotal + vatAmount) };
  });

  constructor() {
    // The partial payment limit depends on the total, so it is checked again whenever the lines change.
    this.lines.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.payment.controls.amountPaid.updateValueAndValidity({ emitEvent: false }));
  }

  ngOnInit(): void {
    this.contactsApi.customerOptions().subscribe({
      next: (customers) => this.customers.set(customers),
      error: (error) => this.error.set(errorMessage(error)),
    });

    if (!this.canWrite()) {
      this.form.disable();
      this.articleSearch.disable();
    }

    const id = this.id();
    if (!id) return;
    this.api.get(Number(id)).subscribe({
      next: (order) => {
        this.order.set(order);
        for (const line of order.lines) this.lines.push(this.lineGroup(line), { emitEvent: false });
        this.form.reset({
          orderDate: order.orderDate,
          customerId: order.customer?.id ?? null,
          notes: order.notes ?? '',
          lines: this.lines.getRawValue(),
          payment: {
            paymentStatus: order.paymentStatus,
            amountPaid: order.paymentStatus === 'PartiallyPaid' ? order.amountPaid : 0,
          },
        });
        if (!this.canWrite()) this.form.disable();
      },
      error: (error) => {
        this.loadFailed.set(true);
        this.error.set(error.status === 404 ? 'This order no longer exists.' : errorMessage(error));
      },
    });
  }

  protected displayArticle(article: ArticleOption | string | null): string {
    return typeof article === 'string' ? article : '';
  }

  /** Adds the chosen article, or one more of it when it is already on the order. */
  protected addArticle(event: MatAutocompleteSelectedEvent): void {
    const article = event.option.value as ArticleOption;
    this.inStock.update((stock) => new Map(stock).set(article.id, article.quantity));

    const existing = this.lines.controls.find((line) => line.controls.articleId.value === article.id);
    if (existing) {
      existing.controls.quantity.setValue(Number(existing.controls.quantity.value) + 1);
    } else {
      this.lines.push(
        this.lineGroup({
          articleId: article.id,
          reference: article.reference,
          name: article.name,
          quantity: 1,
          unitPrice: article.salePrice,
        }),
      );
    }
    this.articleSearch.setValue('');
  }

  protected removeLine(index: number): void {
    this.lines.removeAt(index);
    this.lines.markAsTouched();
  }

  protected lineTotal(line: LineGroup): number {
    return (Number(line.controls.quantity.value) || 0) * roundMoney(Number(line.controls.unitPrice.value) || 0);
  }

  /**
   * How many units this line may still take: stock on hand plus what this order already holds. Unknown until the
   * article is picked in this visit.
   */
  protected available(line: LineGroup): number | null {
    const articleId = line.controls.articleId.value;
    const onHand = this.inStock().get(articleId);
    if (onHand === undefined) return null;
    const held = this.order()?.lines.filter((l) => l.articleId === articleId).reduce((sum, l) => sum + l.quantity, 0) ?? 0;
    return onHand + held;
  }

  protected save(print = false): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      if (this.lines.length === 0) this.error.set('Add at least one article.');
      return;
    }
    const value = this.form.getRawValue();
    const request: SaveOrderRequest = {
      orderDate: value.orderDate,
      customerId: value.customerId,
      notes: value.notes || null,
      lines: value.lines.map((line) => ({
        articleId: line.articleId,
        quantity: Number(line.quantity),
        unitPrice: Number(line.unitPrice),
      })),
      ...paymentRequest(this.payment),
    };
    const existing = this.order();

    this.busy.set(true);
    this.error.set(null);
    const save = existing ? this.api.update(existing.id, request) : this.api.create(request);
    save.subscribe({
      next: (saved) => {
        this.snackBar.open(`${orderNumber(saved.id)} was saved.`, undefined, { duration: 3000 });
        void this.router.navigate(print ? ['/orders', saved.id, 'print'] : ['/orders'], {
          queryParams: print ? { print: true } : {},
        });
      },
      error: (error) => {
        this.busy.set(false);
        this.error.set(applyServerErrors(this.form, error));
      },
    });
  }

  private lineGroup(line: { articleId: number; reference: string; name: string; quantity: number; unitPrice: number }): LineGroup {
    return new FormGroup({
      articleId: new FormControl(line.articleId, { nonNullable: true }),
      reference: new FormControl(line.reference, { nonNullable: true }),
      name: new FormControl(line.name, { nonNullable: true }),
      quantity: new FormControl(line.quantity, { nonNullable: true, validators: QUANTITY }),
      unitPrice: new FormControl(line.unitPrice, { nonNullable: true, validators: PRICE }),
    });
  }
}
