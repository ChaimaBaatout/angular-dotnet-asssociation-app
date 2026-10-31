import { Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { PasswordForm } from './password-form';

/** Shown when an account is on a temporary password. Nothing else is reachable until it is replaced. */
@Component({
  selector: 'app-change-password',
  imports: [PasswordForm, MatButtonModule],
  template: `
    <div class="auth-page">
      <main class="auth-card">
        <h1>Choose a new password</h1>
        <p class="muted">You are using a temporary password. Replace it to continue.</p>
        <app-password-form submitLabel="Save and continue" (changed)="continue()" />
        <button matButton class="full-width" type="button" (click)="auth.logout()">Sign out</button>
      </main>
    </div>
  `,
})
export class ChangePassword {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected continue(): void {
    void this.router.navigateByUrl('/');
  }
}
