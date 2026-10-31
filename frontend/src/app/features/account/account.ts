import { Component, inject, OnInit, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AuthService } from '../../core/auth.service';
import { applyServerErrors } from '../../core/errors';
import { PasswordForm } from '../auth/password-form';
import { QrSignIn } from './qr-sign-in';

@Component({
  selector: 'app-account',
  imports: [
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatSnackBarModule,
    PasswordForm,
    QrSignIn,
  ],
  templateUrl: './account.html',
})
export class Account implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly qrSignIn = viewChild.required(QrSignIn);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    phoneNumber: ['', Validators.maxLength(30)],
  });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    const user = this.auth.user();
    if (user) this.form.reset({ ...user, phoneNumber: user.phoneNumber ?? '' });
  }

  protected save(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const { firstName, lastName, phoneNumber } = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.auth.updateMe({ firstName, lastName, phoneNumber: phoneNumber || null }).subscribe({
      next: () => {
        this.busy.set(false);
        this.form.markAsPristine();
        this.snackBar.open('Your details were saved.', undefined, { duration: 3000 });
      },
      error: (error) => {
        this.busy.set(false);
        this.error.set(applyServerErrors(this.form, error));
      },
    });
  }

  protected passwordChanged(): void {
    this.snackBar.open('Your password was changed. Other devices were signed out and your QR code was cancelled.', undefined, {
      duration: 5000,
    });
    this.qrSignIn().refresh();
  }
}
