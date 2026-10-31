import { DatePipe } from '@angular/common';
import { Component, inject, OnInit, signal, viewChild } from '@angular/core';
import { FormBuilder, FormGroupDirective, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import QRCode from 'qrcode';
import { QrCodeApi } from '../../core/api';
import { applyServerErrors, errorMessage } from '../../core/errors';
import { qrSignInUrl } from '../../core/link-token';
import { QrCodeStatus } from '../../core/models';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/dialogs';

@Component({
  selector: 'app-qr-sign-in',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatDialogModule,
    MatSnackBarModule,
  ],
  templateUrl: './qr-sign-in.html',
  styles: `
    .code {
      display: block;
      width: 280px;
      max-width: 100%;
      height: auto;
      margin: 0 0 16px;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: 8px;
    }
  `,
})
export class QrSignIn implements OnInit {
  private readonly api = inject(QrCodeApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly formDirective = viewChild(FormGroupDirective);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    currentPassword: ['', Validators.required],
  });
  protected readonly status = signal<QrCodeStatus | null>(null);
  /** The picture of the code. It exists only until this section is closed, because the secret cannot be fetched again. */
  protected readonly image = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly isLocalAddress = ['localhost', '127.0.0.1'].includes(location.hostname);

  ngOnInit(): void {
    this.refresh();
  }

  /** Reloads the status, for example after a password change cancels the code. */
  refresh(): void {
    this.image.set(null);
    this.api.status().subscribe({
      next: (status) => this.status.set(status),
      error: (error) => this.error.set(errorMessage(error)),
    });
  }

  protected generate(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.api.create(this.form.getRawValue().currentPassword).subscribe({
      next: async ({ token, expiresAt }) => {
        this.image.set(await QRCode.toDataURL(qrSignInUrl(location.origin, token), { width: 560, margin: 2 }));
        this.status.set({ enabled: true, expiresAt });
        this.busy.set(false);
        this.formDirective()?.resetForm();
      },
      error: (error) => {
        this.busy.set(false);
        this.error.set(applyServerErrors(this.form, error));
      },
    });
  }

  protected remove(): void {
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
        data: {
          title: 'Turn off QR sign-in?',
          message: 'Your current QR code will stop working straight away.',
          confirmLabel: 'Turn off',
        },
      })
      .afterClosed()
      .subscribe((confirmed) => {
        if (!confirmed) return;
        this.api.delete().subscribe({
          next: () => {
            this.image.set(null);
            this.status.set({ enabled: false, expiresAt: null });
            this.snackBar.open('QR sign-in was turned off.', undefined, { duration: 3000 });
          },
          error: (error) => this.error.set(errorMessage(error)),
        });
      });
  }
}
