import { Component, inject, input, output, signal, viewChild } from '@angular/core';
import {
  AbstractControl,
  FormBuilder,
  FormGroupDirective,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { AuthService } from '../../core/auth.service';
import { applyServerErrors } from '../../core/errors';

export function passwordsMatch(group: AbstractControl): ValidationErrors | null {
  return group.get('newPassword')?.value === group.get('confirmPassword')?.value ? null : { mismatch: true };
}

@Component({
  selector: 'app-password-form',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  templateUrl: './password-form.html',
})
export class PasswordForm {
  private readonly auth = inject(AuthService);

  readonly submitLabel = input('Change password');
  readonly changed = output<void>();

  private readonly formDirective = viewChild.required(FormGroupDirective);

  protected readonly form = inject(FormBuilder).nonNullable.group(
    {
      currentPassword: ['', Validators.required],
      newPassword: ['', [Validators.required, Validators.minLength(5), Validators.maxLength(128)]],
      confirmPassword: ['', Validators.required],
    },
    { validators: passwordsMatch },
  );
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected submit(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const { currentPassword, newPassword } = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.auth.changePassword(currentPassword, newPassword).subscribe({
      next: () => {
        this.busy.set(false);
        // Not form.reset(): that leaves the form "submitted", which would show every empty field as an error.
        this.formDirective().resetForm();
        this.changed.emit();
      },
      error: (error) => {
        this.busy.set(false);
        this.error.set(applyServerErrors(this.form, error));
      },
    });
  }
}
