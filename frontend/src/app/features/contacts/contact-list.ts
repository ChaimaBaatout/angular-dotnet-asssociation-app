import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
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
import { ContactsApi } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { errorMessage } from '../../core/errors';
import { Contact } from '../../core/models';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/dialogs';
import { CONTACT_KINDS, ContactKind } from './contact-kinds';

/** The supplier or customer list, depending on the route's kind. */
@Component({
  selector: 'app-contact-list',
  imports: [
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
  templateUrl: './contact-list.html',
  styles: `
    .filters {
      display: grid;
      grid-template-columns: minmax(0, 2fr) minmax(0, 1fr);
      gap: 12px;
      margin-bottom: 16px;
    }

    @media (max-width: 599.98px) {
      .filters {
        grid-template-columns: minmax(0, 1fr);
      }
    }
  `,
})
export class ContactList implements OnInit {
  private readonly api = inject(ContactsApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly auth = inject(AuthService);

  /** Route data. */
  readonly kind = input.required<ContactKind>();
  protected readonly info = computed(() => CONTACT_KINDS[this.kind()]);
  protected readonly canWrite = computed(() => this.auth.can(this.info().write));

  protected readonly columns = ['name', 'contact', 'phone', 'city', 'status', 'actions'];
  protected readonly filters = inject(FormBuilder).nonNullable.group({
    search: [''],
    isActive: [null as boolean | null],
  });

  protected readonly contacts = signal<Contact[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(0);
  protected readonly pageSize = signal(20);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  private readonly reload$ = new Subject<void>();

  constructor() {
    this.reload$
      .pipe(
        tap(() => this.loading.set(true)),
        switchMap(() =>
          this.api
            .list(this.kind(), { ...this.filters.getRawValue(), page: this.page() + 1, pageSize: this.pageSize() })
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
        this.contacts.set(result.items);
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
  }

  protected onPage(event: PageEvent): void {
    this.page.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.reload$.next();
  }

  protected delete(contact: Contact): void {
    const data: ConfirmDialogData = {
      title: `Delete ${this.info().singular}?`,
      message: `${contact.name} will be permanently deleted. This cannot be undone. To keep their details, deactivate them instead.`,
      confirmLabel: 'Delete',
    };
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data })
      .afterClosed()
      .subscribe((confirmed) => {
        if (!confirmed) return;
        this.api.delete(this.kind(), contact.id).subscribe({
          next: () => {
            this.notify(`${contact.name} was deleted.`);
            if (this.contacts().length === 1 && this.page() > 0) this.page.update((p) => p - 1);
            this.reload$.next();
          },
          error: (error) => this.notify(errorMessage(error)),
        });
      });
  }

  private notify(message: string): void {
    this.snackBar.open(message, undefined, { duration: 5000 });
  }
}
