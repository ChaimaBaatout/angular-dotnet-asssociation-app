import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { ProfilesApi } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { applyServerErrors, errorMessage } from '../../core/errors';
import { PermissionInfo, Permissions, Profile } from '../../core/models';

@Component({
  selector: 'app-profile-form',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatCheckboxModule,
    MatButtonModule,
    MatIconModule,
    MatSnackBarModule,
  ],
  templateUrl: './profile-form.html',
  styles: `
    .permission-groups {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 280px), 1fr));
      gap: 16px;
      margin: 0 0 16px;
    }

    fieldset {
      min-width: 0;
      margin: 0;
      padding: 8px 12px;
      border: 1px solid var(--mat-sys-outline-variant);
      border-radius: 8px;
    }

    legend {
      padding: 0 4px;
      font: var(--mat-sys-title-small);
    }

    mat-checkbox {
      display: block;
    }
  `,
})
export class ProfileForm implements OnInit {
  private readonly api = inject(ProfilesApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  /** Route parameter; absent when creating a profile. */
  readonly id = input<string>();

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    description: ['', Validators.maxLength(500)],
  });
  protected readonly profile = signal<Profile | null>(null);
  protected readonly catalog = signal<PermissionInfo[]>([]);
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly loadFailed = signal(false);

  protected readonly canEdit = computed(
    () => (this.auth.user()?.permissions.includes(Permissions.profilesWrite) ?? false) && !this.profile()?.isSystem,
  );
  protected readonly groups = computed(() => {
    const groups = new Map<string, PermissionInfo[]>();
    for (const permission of this.catalog()) {
      groups.set(permission.group, [...(groups.get(permission.group) ?? []), permission]);
    }
    return [...groups].map(([name, permissions]) => ({ name, permissions }));
  });

  ngOnInit(): void {
    this.api.permissions().subscribe({
      next: (catalog) => this.catalog.set(catalog),
      error: (error) => this.error.set(errorMessage(error)),
    });

    if (!this.canEdit()) this.form.disable();

    const id = this.id();
    if (!id) return;
    this.api.get(Number(id)).subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.selected.set(new Set(profile.permissions));
        this.form.reset({ name: profile.name, description: profile.description ?? '' });
        if (!this.canEdit()) this.form.disable();
      },
      error: (error) => {
        this.loadFailed.set(true);
        this.error.set(error.status === 404 ? 'This profile no longer exists.' : errorMessage(error));
      },
    });
  }

  /** You can only grant or remove permissions you hold yourself, which the API also enforces. */
  protected canToggle(code: string): boolean {
    return this.canEdit() && this.auth.can(code);
  }

  protected toggle(code: string, checked: boolean): void {
    const next = new Set(this.selected());
    if (checked) next.add(code);
    else next.delete(code);
    this.selected.set(next);
  }

  protected save(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const { name, description } = this.form.getRawValue();
    const request = { name, description: description || null, permissions: [...this.selected()] };
    const existing = this.profile();

    this.busy.set(true);
    this.error.set(null);
    (existing ? this.api.update(existing.id, request) : this.api.create(request)).subscribe({
      next: () => {
        this.snackBar.open('Profile saved.', undefined, { duration: 3000 });
        void this.router.navigateByUrl('/admin/profiles');
      },
      error: (error) => {
        this.busy.set(false);
        this.error.set(applyServerErrors(this.form, error));
      },
    });
  }
}
