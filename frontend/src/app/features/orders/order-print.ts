import { DatePipe, DecimalPipe } from '@angular/common';
import { afterNextRender, Component, inject, Injector, input, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Title } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { OrdersApi } from '../../core/api';
import { errorMessage } from '../../core/errors';
import { Company, CURRENCY, Order, orderNumber, paymentLabel } from '../../core/models';

/**
 * The order as a printable A4 page, outside the app's menu and toolbar. The browser's print dialog prints it or saves
 * it as a PDF. With ?print=true the dialog opens as soon as the order has loaded.
 */
@Component({
  selector: 'app-order-print',
  imports: [DatePipe, DecimalPipe, RouterLink, MatButtonModule, MatIconModule],
  templateUrl: './order-print.html',
  styleUrl: './order-print.scss',
})
export class OrderPrint implements OnInit {
  private readonly api = inject(OrdersApi);
  private readonly title = inject(Title);
  private readonly injector = inject(Injector);

  /** Route parameter. */
  readonly id = input.required<string>();
  /** Query parameter. */
  readonly print = input<string>();

  protected readonly currency = CURRENCY;
  protected readonly paymentLabel = paymentLabel;
  protected readonly order = signal<Order | null>(null);
  protected readonly company = signal<Company | null>(null);
  protected readonly number = signal('');
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.number.set(orderNumber(Number(this.id())));
    forkJoin({ order: this.api.get(Number(this.id())), company: this.api.company() }).subscribe({
      next: ({ order, company }) => {
        this.order.set(order);
        this.company.set(company);
        // Browsers suggest the page title as the PDF's file name.
        this.title.setTitle(`${this.number()} ${order.customer?.name ?? ''}`.trim());
        if (this.print() === 'true') afterNextRender(() => window.print(), { injector: this.injector });
      },
      error: (error) =>
        this.error.set(error.status === 404 ? 'This order no longer exists.' : errorMessage(error)),
    });
  }

  protected printNow(): void {
    window.print();
  }
}
