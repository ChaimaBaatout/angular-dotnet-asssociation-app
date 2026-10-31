import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { catchError, debounceTime, EMPTY, Subject, switchMap, tap } from 'rxjs';
import { ProfilesApi, UsersApi } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { errorMessage } from '../../core/errors';
import { Permissions, ProfileOption, User } from '../../core/models';
import { ConfirmDialog, ConfirmDialogData, TemporaryPasswordDialog } from '../../shared/dialogs';

@Component({
  selector: 'app-user-list',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    MatTableModule,
    MatPaginatorModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatDialogModule,
    MatSnackBarModule,
    MatProgressBarModule,
  ],
  templateUrl: './user-list.html',
  styleUrl: './user-list.scss',
})
export class UserList implements OnInit {
  private readonly api = inject(UsersApi);
  private readonly profilesApi = inject(ProfilesApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  protected readonly auth = inject(AuthService);

  protected readonly columns = ['name', 'profile', 'status', 'lastLogin', 'actions'];
  protected readonly filters = inject(FormBuilder).nonNullable.group({
    search: [''],
    profileId: [null as number | null],
    isActive: [null as boolean | null],
  });

  protected readonly users = signal<User[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(0);
  protected readonly pageSize = signal(20);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly profiles = signal<ProfileOption[]>([]);
  protected readonly canWrite = computed(
    () => this.auth.user()?.permissions.includes(Permissions.usersWrite) ?? false,
  );

  private readonly reload$ = new Subject<void>();

  constructor() {
    this.reload$
      .pipe(
        tap(() => this.loading.set(true)),
        switchMap(() =>
          this.api
            .list({ ...this.filters.getRawValue(), page: this.page() + 1, pageSize: this.pageSize() })
            .pipe(
              catchError((error) => {
                this.error.set(errorMessage(error));
                this.loading.set(false);
                return EMPTY;
              }),
            ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((result) => {
        this.error.set(null);
        this.users.set(result.items);
        this.total.set(result.total);
        this.loading.set(false);
      });

    this.filters.valueChanges.pipe(debounceTime(300), takeUntilDestroyed()).subscribe(() => {
      this.page.set(0);
      this.reload$.next();
    });
  }

  ngOnInit(): void {
    this.reload$.next();
    this.profilesApi.options().subscribe((profiles) => this.profiles.set(profiles));
  }

  protected onPage(event: PageEvent): void {
    this.page.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.reload$.next();
  }

  protected isSelf(user: User): boolean {
    return user.id === this.auth.user()?.id;
  }

  protected resetPassword(user: User): void {
    this.confirm({
      title: 'Reset password?',
      message: `${user.email} will be signed out and must sign in with a new temporary password. This also unlocks the account.`,
      confirmLabel: 'Reset password',
    }).subscribe((confirmed) => {
      if (!confirmed) return;
      this.api.resetPassword(user.id).subscribe({
        next: ({ temporaryPassword }) => {
          this.dialog.open(TemporaryPasswordDialog, {
            data: { email: user.email, password: temporaryPassword },
            disableClose: true,
          });
          this.reload$.next();
        },
        error: (error) => this.notify(errorMessage(error)),
      });
    });
  }

  protected delete(user: User): void {
    this.confirm({
      title: 'Delete user?',
      message: `${user.email} will be permanently deleted. This cannot be undone. To keep their history, deactivate them instead.`,
      confirmLabel: 'Delete',
    }).subscribe((confirmed) => {
      if (!confirmed) return;
      this.api.delete(user.id).subscribe({
        next: () => {
          this.notify('User deleted.');
          if (this.users().length === 1 && this.page() > 0) this.page.update((p) => p - 1);
          this.reload$.next();
        },
        error: (error) => this.notify(errorMessage(error)),
      });
    });
  }

  private confirm(data: ConfirmDialogData) {
    return this.dialog.open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data }).afterClosed();
  }

  private notify(message: string): void {
    this.snackBar.open(message, undefined, { duration: 4000 });
  }
}
