import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { applyServerErrors } from '../../core/errors';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink, MatFormFieldModule, MatInputModule, MatButtonModule],
  templateUrl: './register.html',
})
export class Register {
  private readonly auth = inject(AuthService);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
  });
  protected readonly busy = signal(false);
  protected readonly sentTo = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  protected submit(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const request = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.auth.register(request).subscribe({
      next: () => this.sentTo.set(request.email),
      error: (error) => {
        this.busy.set(false);
        this.error.set(applyServerErrors(this.form, error));
      },
    });
  }
}
