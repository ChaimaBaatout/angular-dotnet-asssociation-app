import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { ProfilesApi, UsersApi } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { applyServerErrors, errorMessage } from '../../core/errors';
import { Permissions, ProfileOption, SaveUserRequest, User } from '../../core/models';
import { TemporaryPasswordDialog } from '../../shared/dialogs';

@Component({
  selector: 'app-user-form',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatButtonModule,
    MatIconModule,
    MatDialogModule,
    MatSnackBarModule,
  ],
  templateUrl: './user-form.html',
})
export class UserForm implements OnInit {
  private readonly api = inject(UsersApi);
  private readonly profilesApi = inject(ProfilesApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  /** Route parameter; absent when creating a user. */
  readonly id = input<string>();

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    phoneNumber: ['', Validators.maxLength(30)],
    profileId: [null as number | null, Validators.required],
    isActive: [true],
  });
  protected readonly user = signal<User | null>(null);
  protected readonly profiles = signal<ProfileOption[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly loadFailed = signal(false);

  protected readonly canWrite = computed(
    () => this.auth.user()?.permissions.includes(Permissions.usersWrite) ?? false,
  );
  protected readonly isSelf = computed(() => this.user()?.id === this.auth.user()?.id);

  ngOnInit(): void {
    this.profilesApi.options().subscribe({
      next: (profiles) => this.profiles.set(profiles),
      error: (error) => this.error.set(errorMessage(error)),
    });

    if (!this.canWrite()) this.form.disable();

    const id = this.id();
    if (!id) return;
    this.api.get(Number(id)).subscribe({
      next: (user) => {
        this.user.set(user);
        this.form.reset({ ...user, phoneNumber: user.phoneNumber ?? '' });
        if (this.isSelf()) {
          this.form.controls.profileId.disable();
          this.form.controls.isActive.disable();
        }
      },
      error: (error) => {
        this.loadFailed.set(true);
        this.error.set(error.status === 404 ? 'This user no longer exists.' : errorMessage(error));
      },
    });
  }

  protected save(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const request: SaveUserRequest = { ...value, profileId: value.profileId!, phoneNumber: value.phoneNumber || null };
    const existing = this.user();

    this.busy.set(true);
    this.error.set(null);
    if (existing) {
      this.api.update(existing.id, request).subscribe({
        next: () => {
          this.snackBar.open('User saved.', undefined, { duration: 3000 });
          this.backToList();
        },
        error: (error) => this.showError(error),
      });
    } else {
      this.api.create(request).subscribe({
        next: ({ user, temporaryPassword }) => {
          this.dialog
            .open(TemporaryPasswordDialog, {
              data: { email: user.email, password: temporaryPassword },
              disableClose: true,
            })
            .afterClosed()
            .subscribe(() => this.backToList());
        },
        error: (error) => this.showError(error),
      });
    }
  }

  private showError(error: unknown): void {
    this.busy.set(false);
    this.error.set(applyServerErrors(this.form, error));
  }

  private backToList(): void {
    void this.router.navigateByUrl('/admin/users');
  }
}
