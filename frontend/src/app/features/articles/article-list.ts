import { DecimalPipe } from '@angular/common';
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
import { ArticlesApi, ContactsApi } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { errorMessage } from '../../core/errors';
import { Article, ContactOption, CURRENCY, Permissions } from '../../core/models';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/dialogs';

@Component({
  selector: 'app-article-list',
  imports: [
    DecimalPipe,
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
  templateUrl: './article-list.html',
  styles: `
    .filters {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 170px), 1fr));
      gap: 12px;
      margin-bottom: 16px;
    }

    .search {
      grid-column: span 2;
    }

    .stock {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      white-space: nowrap;
    }

    @media (max-width: 599.98px) {
      .search {
        grid-column: 1 / -1;
      }
    }
  `,
})
export class ArticleList implements OnInit {
  private readonly api = inject(ArticlesApi);
  private readonly contactsApi = inject(ContactsApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly auth = inject(AuthService);

  /** Query parameter, so other pages can link straight to the articles running low. */
  readonly lowStock = input<string>();

  protected readonly currency = CURRENCY;
  protected readonly columns = ['article', 'supplier', 'stock', 'price', 'actions'];
  protected readonly filters = inject(FormBuilder).nonNullable.group({
    search: [''],
    category: [null as string | null],
    supplierId: [null as number | null],
    lowStock: [null as boolean | null],
    isActive: [null as boolean | null],
  });

  protected readonly articles = signal<Article[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(0);
  protected readonly pageSize = signal(20);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly categories = signal<string[]>([]);
  protected readonly suppliers = signal<ContactOption[]>([]);
  protected readonly canWrite = computed(() => this.auth.can(Permissions.articlesWrite));

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
        this.articles.set(result.items);
        this.total.set(result.total);
        this.loading.set(false);
      });

    this.filters.valueChanges.pipe(debounceTime(300), takeUntilDestroyed()).subscribe(() => {
      this.page.set(0);
      this.reload$.next();
    });
  }

  ngOnInit(): void {
    if (this.lowStock() === 'true') this.filters.patchValue({ lowStock: true }, { emitEvent: false });
    this.reload$.next();
    this.api.categories().subscribe((categories) => this.categories.set(categories));
    this.contactsApi.supplierOptions().subscribe((suppliers) => this.suppliers.set(suppliers));
  }

  protected onPage(event: PageEvent): void {
    this.page.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    this.reload$.next();
  }

  protected delete(article: Article): void {
    const data: ConfirmDialogData = {
      title: 'Delete article?',
      message: `${article.reference} – ${article.name} will be permanently deleted. This cannot be undone. To keep it in the catalogue, deactivate it instead.`,
      confirmLabel: 'Delete',
    };
    this.dialog
      .open<ConfirmDialog, ConfirmDialogData, boolean>(ConfirmDialog, { data })
      .afterClosed()
      .subscribe((confirmed) => {
        if (!confirmed) return;
        this.api.delete(article.id).subscribe({
          next: () => {
            this.notify(`${article.reference} was deleted.`);
            if (this.articles().length === 1 && this.page() > 0) this.page.update((p) => p - 1);
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
