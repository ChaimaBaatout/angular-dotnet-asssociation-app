import { DecimalPipe } from '@angular/common';
import { Component, inject, input, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { OrdersApi } from '../../core/api';
import { applyServerErrors } from '../../core/errors';
import { CURRENCY, PAYMENT_STATUSES, PaymentStatus, SavePaymentRequest } from '../../core/models';

export type PaymentGroup = FormGroup<{
  paymentStatus: FormControl<PaymentStatus>;
  amountPaid: FormControl<number>;
}>;

/**
 * The payment status, and the amount paid when only part is. `total` is read on each check, so a form whose total
 * changes should call updateValueAndValidity() on the group when it does.
 */
export function paymentGroup(total: () => number, status: PaymentStatus = 'NotPaid', amountPaid = 0): PaymentGroup {
  const partialAmount = (control: AbstractControl): ValidationErrors | null => {
    if (control.parent?.get('paymentStatus')?.value !== 'PartiallyPaid') return null;
    if (!/^\d+(\.\d{1,3})?$/.test(String(control.value ?? ''))) return { format: true };
    const amount = Number(control.value);
    return amount > 0 && amount < total() ? null : { range: true };
  };
  const group: PaymentGroup = new FormGroup({
    paymentStatus: new FormControl(status, { nonNullable: true }),
    amountPaid: new FormControl(status === 'PartiallyPaid' ? amountPaid : 0, {
      nonNullable: true,
      validators: partialAmount,
    }),
  });
  group.controls.paymentStatus.valueChanges.subscribe(() => group.controls.amountPaid.updateValueAndValidity());
  return group;
}

export function paymentRequest(group: PaymentGroup): SavePaymentRequest {
  const { paymentStatus, amountPaid } = group.getRawValue();
  return { paymentStatus, amountPaid: paymentStatus === 'PartiallyPaid' ? Number(amountPaid) : 0 };
}

@Component({
  selector: 'app-payment-fields',
  imports: [DecimalPipe, ReactiveFormsModule, MatRadioModule, MatFormFieldModule, MatInputModule],
  template: `
    <mat-radio-group class="statuses" [formControl]="group().controls.paymentStatus" aria-label="Payment status">
      @for (status of statuses; track status.value) {
        <mat-radio-button [value]="status.value">{{ status.label }}</mat-radio-button>
      }
    </mat-radio-group>

    @switch (group().controls.paymentStatus.value) {
      @case ('PartiallyPaid') {
        <mat-form-field class="amount">
          <mat-label>Amount paid</mat-label>
          <input matInput type="number" min="0" step="0.001" [formControl]="group().controls.amountPaid" />
          <span matTextSuffix>{{ currency }}</span>
          @if (group().controls.amountPaid.valid) {
            <mat-hint>Still due: {{ total() - group().controls.amountPaid.value | number: '1.3-3' }} {{ currency }}</mat-hint>
          }
          <mat-error>
            {{
              group().controls.amountPaid.getError('server') ??
                'Enter more than 0 and less than the total of ' + (total() | number: '1.3-3') + ', with up to 3 decimals.'
            }}
          </mat-error>
        </mat-form-field>
      }
      @case ('Paid') {
        <p class="muted">Paid in full: {{ total() | number: '1.3-3' }} {{ currency }}</p>
      }
      @default {
        <p class="muted">Still due: {{ total() | number: '1.3-3' }} {{ currency }}</p>
      }
    }
  `,
  styles: `
    .statuses {
      display: flex;
      flex-wrap: wrap;
      gap: 4px 16px;
      margin: 0 0 12px -10px;
    }

    .amount {
      width: min(100%, 280px);
    }

    p {
      margin: 0 0 8px;
    }
  `,
})
export class PaymentFields {
  readonly group = input.required<PaymentGroup>();
  readonly total = input.required<number>();

  protected readonly statuses = PAYMENT_STATUSES;
  protected readonly currency = CURRENCY;
}

export interface PaymentDialogData {
  orderId: number;
  number: string;
  total: number;
  paymentStatus: PaymentStatus;
  amountPaid: number;
}

/** Records a payment from the orders list. Closes with true once saved. */
@Component({
  selector: 'app-payment-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, PaymentFields],
  template: `
    <h2 mat-dialog-title>Payment for {{ data.number }}</h2>
    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <mat-dialog-content>
        @if (error(); as message) {
          <p class="form-error" role="alert">{{ message }}</p>
        }
        <app-payment-fields [group]="form" [total]="data.total" />
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button matButton type="button" mat-dialog-close>Cancel</button>
        <button matButton="filled" type="submit" [disabled]="busy()">Save</button>
      </mat-dialog-actions>
    </form>
  `,
})
export class PaymentDialog {
  private readonly api = inject(OrdersApi);
  private readonly dialogRef = inject<MatDialogRef<PaymentDialog, boolean>>(MatDialogRef);

  protected readonly data = inject<PaymentDialogData>(MAT_DIALOG_DATA);
  protected readonly form = paymentGroup(() => this.data.total, this.data.paymentStatus, this.data.amountPaid);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected save(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.api.updatePayment(this.data.orderId, paymentRequest(this.form)).subscribe({
      next: () => this.dialogRef.close(true),
      error: (error) => {
        this.busy.set(false);
        this.error.set(applyServerErrors(this.form, error));
      },
    });
  }
}
