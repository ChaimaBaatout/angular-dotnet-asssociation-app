import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { applyServerErrors } from '../../core/errors';
import { tokenFromFragment } from '../../core/link-token';
import { passwordsMatch } from './password-form';

/** Where emailed links land, both to finish signing up and after a forgotten password. */
@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink, MatFormFieldModule, MatInputModule, MatButtonModule],
  templateUrl: './reset-password.html',
})
export class ResetPassword {
  private readonly auth = inject(AuthService);

  protected readonly token = tokenFromFragment(inject(ActivatedRoute).snapshot.fragment);
  protected readonly form = inject(FormBuilder).nonNullable.group(
    {
      newPassword: ['', [Validators.required, Validators.minLength(5), Validators.maxLength(128)]],
      confirmPassword: ['', Validators.required],
    },
    { validators: passwordsMatch },
  );
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected submit(): void {
    if (this.form.invalid || this.busy() || !this.token) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.auth.resetPassword(this.token, this.form.getRawValue().newPassword).subscribe({
      // Any session in this browser has just been ended by the server.
      next: () => this.auth.endSession('/login?passwordSet=1'),
      error: (error) => {
        this.busy.set(false);
        this.error.set(applyServerErrors(this.form, error));
      },
    });
  }
}
