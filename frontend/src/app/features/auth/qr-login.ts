import { Location } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { errorMessage } from '../../core/errors';
import { tokenFromFragment } from '../../core/link-token';

/** Where a phone lands after scanning an account's QR code with its camera. */
@Component({
  selector: 'app-qr-login',
  imports: [RouterLink, MatButtonModule],
  template: `
    <div class="auth-page">
      <main class="auth-card">
        @if (error(); as message) {
          <h1>Could not sign in</h1>
          <p class="form-error" role="alert">{{ message }}</p>
          <a matButton="filled" class="full-width" routerLink="/login">Go to sign in</a>
        } @else {
          <h1>Signing in…</h1>
        }
      </main>
    </div>
  `,
})
export class QrLogin implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);

  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    const token = tokenFromFragment(this.route.snapshot.fragment);
    // The code keeps working for weeks, so keep its secret out of the browser's history.
    this.location.replaceState('/qr-login');
    if (!token) {
      this.error.set('This QR code is not valid.');
      return;
    }
    this.auth.qrLogin(token).subscribe({
      next: () => void this.router.navigateByUrl('/'),
      error: (error) => this.error.set(errorMessage(error)),
    });
  }
}
