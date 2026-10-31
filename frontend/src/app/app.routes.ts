import { Routes } from '@angular/router';
import { authGuard, guestGuard, mustChangePasswordGuard, permissionGuard } from './core/guards';
import { Permissions } from './core/models';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    title: 'Sign in',
    loadComponent: () => import('./features/auth/login').then((m) => m.Login),
  },
  {
    path: 'register',
    canActivate: [guestGuard],
    title: 'Create an account',
    loadComponent: () => import('./features/auth/register').then((m) => m.Register),
  },
  {
    path: 'forgot-password',
    canActivate: [guestGuard],
    title: 'Forgot password',
    loadComponent: () => import('./features/auth/forgot-password').then((m) => m.ForgotPassword),
  },
  {
    path: 'reset-password',
    title: 'Choose a password',
    loadComponent: () => import('./features/auth/reset-password').then((m) => m.ResetPassword),
  },
  {
    path: 'qr-scan',
    canActivate: [guestGuard],
    title: 'Sign in with a QR code',
    loadComponent: () => import('./features/auth/qr-scan').then((m) => m.QrScan),
  },
  {
    path: 'qr-login',
    title: 'Signing in',
    loadComponent: () => import('./features/auth/qr-login').then((m) => m.QrLogin),
  },
  {
    path: 'change-password',
    canActivate: [mustChangePasswordGuard],
    title: 'Choose a new password',
    loadComponent: () => import('./features/auth/change-password').then((m) => m.ChangePassword),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      {
        path: '',
        pathMatch: 'full',
        title: 'Home',
        loadComponent: () => import('./features/home/home').then((m) => m.Home),
      },
      {
        path: 'account',
        title: 'My account',
        loadComponent: () => import('./features/account/account').then((m) => m.Account),
      },
      {
        path: 'admin/users',
        canActivate: [permissionGuard(Permissions.usersRead)],
        title: 'Users',
        loadComponent: () => import('./features/users/user-list').then((m) => m.UserList),
      },
      {
        path: 'admin/users/new',
        canActivate: [permissionGuard(Permissions.usersWrite)],
        title: 'New user',
        loadComponent: () => import('./features/users/user-form').then((m) => m.UserForm),
      },
      {
        path: 'admin/users/:id',
        canActivate: [permissionGuard(Permissions.usersRead)],
        title: 'User details',
        loadComponent: () => import('./features/users/user-form').then((m) => m.UserForm),
      },
      {
        path: 'admin/profiles',
        canActivate: [permissionGuard(Permissions.profilesRead)],
        title: 'Profiles',
        loadComponent: () => import('./features/profiles/profile-list').then((m) => m.ProfileList),
      },
      {
        path: 'admin/profiles/new',
        canActivate: [permissionGuard(Permissions.profilesWrite)],
        title: 'New profile',
        loadComponent: () => import('./features/profiles/profile-form').then((m) => m.ProfileForm),
      },
      {
        path: 'admin/profiles/:id',
        canActivate: [permissionGuard(Permissions.profilesRead)],
        title: 'Profile details',
        loadComponent: () => import('./features/profiles/profile-form').then((m) => m.ProfileForm),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
