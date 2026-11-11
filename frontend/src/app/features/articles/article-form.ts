import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { ArticlesApi, ContactsApi } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { applyServerErrors, errorMessage } from '../../core/errors';
import { Article, ContactOption, CURRENCY, Permissions, SaveArticleRequest } from '../../core/models';

const PRICE = [Validators.required, Validators.min(0), Validators.max(1_000_000_000), Validators.pattern(/^\d+(\.\d{1,3})?$/)];
const COUNT = [Validators.required, Validators.min(0), Validators.max(1_000_000_000), Validators.pattern(/^\d+$/)];

@Component({
  selector: 'app-article-form',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    MatAutocompleteModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatButtonModule,
    MatIconModule,
    MatSnackBarModule,
  ],
  templateUrl: './article-form.html',
})
export class ArticleForm implements OnInit {
  private readonly api = inject(ArticlesApi);
  private readonly contactsApi = inject(ContactsApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  /** Route parameter; absent when creating. */
  readonly id = input<string>();

  protected readonly currency = CURRENCY;
  protected readonly canWrite = computed(() => this.auth.can(Permissions.articlesWrite));

  protected readonly form = inject(FormBuilder).nonNullable.group({
    reference: ['', [Validators.required, Validators.maxLength(50)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    brand: ['', Validators.maxLength(100)],
    category: ['', Validators.maxLength(100)],
    location: ['', Validators.maxLength(50)],
    supplierId: [null as number | null],
    compatibility: ['', Validators.maxLength(500)],
    description: ['', Validators.maxLength(1000)],
    purchasePrice: [0, PRICE],
    salePrice: [0, PRICE],
    quantity: [0, COUNT],
    minimumStock: [0, COUNT],
    isActive: [true],
  });
  protected readonly article = signal<Article | null>(null);
  protected readonly suppliers = signal<ContactOption[]>([]);
  protected readonly categories = signal<string[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly loadFailed = signal(false);

  private readonly categoryText = toSignal(this.form.controls.category.valueChanges, { initialValue: '' });
  protected readonly categorySuggestions = computed(() => {
    const text = this.categoryText().trim().toLowerCase();
    return this.categories().filter((c) => c.toLowerCase().includes(text) && c.toLowerCase() !== text);
  });

  ngOnInit(): void {
    this.contactsApi.supplierOptions().subscribe({
      next: (suppliers) => this.suppliers.set(suppliers),
      error: (error) => this.error.set(errorMessage(error)),
    });
    this.api.categories().subscribe((categories) => this.categories.set(categories));

    if (!this.canWrite()) this.form.disable();

    const id = this.id();
    if (!id) return;
    this.api.get(Number(id)).subscribe({
      next: (article) => {
        this.article.set(article);
        this.form.reset({
          ...article,
          brand: article.brand ?? '',
          category: article.category ?? '',
          location: article.location ?? '',
          compatibility: article.compatibility ?? '',
          description: article.description ?? '',
        });
      },
      error: (error) => {
        this.loadFailed.set(true);
        this.error.set(error.status === 404 ? 'This article no longer exists.' : errorMessage(error));
      },
    });
  }

  protected save(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const request: SaveArticleRequest = {
      ...value,
      brand: value.brand || null,
      category: value.category || null,
      location: value.location || null,
      compatibility: value.compatibility || null,
      description: value.description || null,
    };
    const existing = this.article();

    this.busy.set(true);
    this.error.set(null);
    const save = existing ? this.api.update(existing.id, request) : this.api.create(request);
    save.subscribe({
      next: (saved) => {
        this.snackBar.open(`${saved.reference} was saved.`, undefined, { duration: 3000 });
        void this.router.navigateByUrl('/articles');
      },
      error: (error) => {
        this.busy.set(false);
        this.error.set(applyServerErrors(this.form, error));
      },
    });
  }
}
