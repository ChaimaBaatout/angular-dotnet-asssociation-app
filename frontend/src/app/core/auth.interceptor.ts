import { HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** Attaches the access token and, when it has expired, refreshes it and retries the request once. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const isAuthCall = req.url.startsWith('/api/auth/');
  if (!req.url.startsWith('/api/') || isAuthCall) return next(req);

  const auth = inject(AuthService);
  const withToken = (request: HttpRequest<unknown>) => {
    const token = auth.accessToken();
    return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
  };

  return next(withToken(req)).pipe(
    catchError((error) => {
      if (error.status !== 401 || !auth.user()) return throwError(() => error);
      return auth.refresh().pipe(
        catchError(() => {
          auth.endSession();
          return throwError(() => error);
        }),
        switchMap(() => next(withToken(req))),
      );
    }),
  );
};
