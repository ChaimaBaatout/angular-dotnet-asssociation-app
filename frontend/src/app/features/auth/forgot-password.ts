import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { applyServerErrors } from '../../core/errors';

@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, RouterLink, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
    <div class="auth-page">
      <main class="auth-card">
        @if (sentTo(); as email) {
          <h1>Check your email</h1>
          <p class="muted">
            If <strong>{{ email }}</strong> has an account, we sent it a link to choose a new password. The link expires
            in 1 hour.
          </p>
          <a matButton="filled" class="full-width" routerLink="/login">Back to sign in</a>
        } @else {
          <h1>Forgot password</h1>
          <p class="muted">Enter your email and we will send you a link to choose a new password.</p>

          @if (error(); as message) {
            <p class="form-error" role="alert">{{ message }}</p>
          }

          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <mat-form-field class="full-width">
              <mat-label>Email</mat-label>
              <input matInput type="email" formControlName="email" autocomplete="username" />
              <mat-error>{{ form.controls.email.getError('server') ?? 'Enter a valid email address.' }}</mat-error>
            </mat-form-field>
            <button matButton="filled" class="full-width" type="submit" [disabled]="busy()">Send link</button>
          </form>

          <p class="auth-links">
            <a routerLink="/login">Back to sign in</a>
          </p>
        }
      </main>
    </div>
  `,
})
export class ForgotPassword {
  private readonly auth = inject(AuthService);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });
  protected readonly busy = signal(false);
  protected readonly sentTo = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected submit(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const { email } = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.auth.forgotPassword(email).subscribe({
      next: () => this.sentTo.set(email),
      error: (error) => {
        this.busy.set(false);
        this.error.set(applyServerErrors(this.form, error));
      },
    });
  }
}
