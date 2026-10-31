import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Requires a session, and sends users on a temporary password to change it first. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const user = auth.user();
  if (!user) return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  return user.mustChangePassword ? router.createUrlTree(['/change-password']) : true;
};

export const guestGuard: CanActivateFn = () => {
  return inject(AuthService).user() ? inject(Router).createUrlTree(['/']) : true;
};

export const mustChangePasswordGuard: CanActivateFn = () => {
  const user = inject(AuthService).user();
  const router = inject(Router);
  if (!user) return router.createUrlTree(['/login']);
  return user.mustChangePassword ? true : router.createUrlTree(['/']);
};

export const permissionGuard = (permission: string): CanActivateFn => {
  return () => (inject(AuthService).can(permission) ? true : inject(Router).createUrlTree(['/']));
};
