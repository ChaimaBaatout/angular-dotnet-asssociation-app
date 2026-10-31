import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, finalize, firstValueFrom, Observable, of, shareReplay, tap } from 'rxjs';
import { CurrentUser, RegisterRequest, SessionResponse } from './models';

/**
 * Holds the signed-in session. The access token lives only in memory; the refresh token is an
 * HttpOnly cookie that scripts cannot read, so a page reload restores the session via refresh().
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly token = signal<string | null>(null);
  private readonly currentUser = signal<CurrentUser | null>(null);
  private refreshInFlight: Observable<SessionResponse> | null = null;

  readonly accessToken = this.token.asReadonly();
  readonly user = this.currentUser.asReadonly();
  readonly displayName = computed(() => {
    const user = this.currentUser();
    return user ? `${user.firstName} ${user.lastName}` : '';
  });

  can(permission: string): boolean {
    return this.currentUser()?.permissions.includes(permission) ?? false;
  }

  login(email: string, password: string): Observable<SessionResponse> {
    return this.http
      .post<SessionResponse>('/api/auth/login', { email, password })
      .pipe(tap((session) => this.setSession(session)));
  }

  qrLogin(token: string): Observable<SessionResponse> {
    return this.http
      .post<SessionResponse>('/api/auth/qr-login', { token })
      .pipe(tap((session) => this.setSession(session)));
  }

  register(request: RegisterRequest): Observable<void> {
    return this.http.post<void>('/api/auth/register', request);
  }

  forgotPassword(email: string): Observable<void> {
    return this.http.post<void>('/api/auth/forgot-password', { email });
  }

  resetPassword(token: string, newPassword: string): Observable<void> {
    return this.http.post<void>('/api/auth/reset-password', { token, newPassword });
  }

  /** Concurrent callers share one request, because refresh tokens are single use. */
  refresh(): Observable<SessionResponse> {
    this.refreshInFlight ??= this.http.post<SessionResponse>('/api/auth/refresh', null).pipe(
      tap((session) => this.setSession(session)),
      finalize(() => (this.refreshInFlight = null)),
      shareReplay(1),
    );
    return this.refreshInFlight;
  }

  restoreSession(): Promise<unknown> {
    return firstValueFrom(this.refresh().pipe(catchError(() => of(null))));
  }

  logout(): void {
    this.http.post('/api/auth/logout', null).subscribe({
      next: () => this.endSession(),
      error: () => this.endSession(),
    });
  }

  /** Clears local state and returns to the sign-in page. */
  endSession(url = '/login'): void {
    this.token.set(null);
    this.currentUser.set(null);
    void this.router.navigateByUrl(url);
  }

  changePassword(currentPassword: string, newPassword: string): Observable<SessionResponse> {
    return this.http
      .post<SessionResponse>('/api/me/password', { currentPassword, newPassword })
      .pipe(tap((session) => this.setSession(session)));
  }

  updateMe(details: Pick<CurrentUser, 'firstName' | 'lastName' | 'phoneNumber'>): Observable<CurrentUser> {
    return this.http.put<CurrentUser>('/api/me', details).pipe(tap((user) => this.currentUser.set(user)));
  }

  private setSession(session: SessionResponse): void {
    this.token.set(session.accessToken);
    this.currentUser.set(session.user);
  }
}
