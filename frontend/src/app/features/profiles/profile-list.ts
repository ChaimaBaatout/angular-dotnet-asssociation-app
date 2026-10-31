import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { ProfilesApi } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { errorMessage } from '../../core/errors';
import { Permissions, Profile } from '../../core/models';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/dialogs';

@Component({
  selector: 'app-profile-list',
  imports: [
    RouterLink,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatMenuModule,
    MatDialogModule,
    MatSnackBarModule,
    MatProgressBarModule,
  ],
  templateUrl: './profile-list.html',
  styles: `
    .name {
      color: var(--mat-sys-primary);
      font-weight: 500;
      text-decoration: none;

      &:hover {
        text-decoration: underline;
      }
    }

    .system {
      margin-left: 8px;
    }
  `,
})
export class ProfileList implements OnInit {
  private readonly api = inject(ProfilesApi);
  private readonly auth = inject(AuthService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly columns = ['name', 'description', 'permissions', 'users', 'actions'];
  protected readonly profiles = signal<Profile[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly canWrite = computed(
    () => this.auth.user()?.permissions.includes(Permissions.profilesWrite) ?? false,
  );

  ngOnInit(): void {
    this.load();
  }

  protected delete(profile: Profile): void {
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, {
        data: {
          title: 'Delete profile?',
          message: `The ${profile.name} profile will be permanently deleted. This cannot be undone.`,
          confirmLabel: 'Delete',
        },
      })
      .afterClosed()
      .subscribe((confirmed) => {
        if (!confirmed) return;
        this.api.delete(profile.id).subscribe({
          next: () => {
            this.snackBar.open('Profile deleted.', undefined, { duration: 3000 });
            this.load();
          },
          error: (error) => this.snackBar.open(errorMessage(error), undefined, { duration: 5000 }),
        });
      });
  }

  private load(): void {
    this.loading.set(true);
    this.api.list().subscribe({
      next: (profiles) => {
        this.profiles.set(profiles);
        this.error.set(null);
        this.loading.set(false);
      },
      error: (error) => {
        this.error.set(errorMessage(error));
        this.loading.set(false);
      },
    });
  }
}
