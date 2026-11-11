import { DatePipe } from '@angular/common';
import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { ContactsApi } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { applyServerErrors, errorMessage } from '../../core/errors';
import { Contact, SaveContactRequest } from '../../core/models';
import { CONTACT_KINDS, ContactKind } from './contact-kinds';

@Component({
  selector: 'app-contact-form',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatSlideToggleModule,
    MatButtonModule,
    MatIconModule,
    MatSnackBarModule,
  ],
  templateUrl: './contact-form.html',
})
export class ContactForm implements OnInit {
  private readonly api = inject(ContactsApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  /** Route data. */
  readonly kind = input.required<ContactKind>();
  /** Route parameter; absent when creating. */
  readonly id = input<string>();

  protected readonly info = computed(() => CONTACT_KINDS[this.kind()]);
  protected readonly canWrite = computed(() => this.auth.can(this.info().write));
  protected readonly listUrl = computed(() => `/${this.kind()}`);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    contactName: ['', Validators.maxLength(200)],
    email: ['', [Validators.email, Validators.maxLength(256)]],
    phone: ['', Validators.maxLength(30)],
    address: ['', Validators.maxLength(300)],
    city: ['', Validators.maxLength(100)],
    taxNumber: ['', Validators.maxLength(50)],
    notes: ['', Validators.maxLength(1000)],
    isActive: [true],
  });
  protected readonly contact = signal<Contact | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly loadFailed = signal(false);

  ngOnInit(): void {
    if (!this.canWrite()) this.form.disable();

    const id = this.id();
    if (!id) return;
    this.api.get(this.kind(), Number(id)).subscribe({
      next: (contact) => {
        this.contact.set(contact);
        this.form.reset({
          name: contact.name,
          contactName: contact.contactName ?? '',
          email: contact.email ?? '',
          phone: contact.phone ?? '',
          address: contact.address ?? '',
          city: contact.city ?? '',
          taxNumber: contact.taxNumber ?? '',
          notes: contact.notes ?? '',
          isActive: contact.isActive,
        });
      },
      error: (error) => {
        this.loadFailed.set(true);
        this.error.set(error.status === 404 ? `This ${this.info().singular} no longer exists.` : errorMessage(error));
      },
    });
  }

  protected save(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    const request: SaveContactRequest = {
      name: value.name,
      contactName: value.contactName || null,
      email: value.email || null,
      phone: value.phone || null,
      address: value.address || null,
      city: value.city || null,
      taxNumber: value.taxNumber || null,
      notes: value.notes || null,
      isActive: value.isActive,
    };
    const existing = this.contact();

    this.busy.set(true);
    this.error.set(null);
    const save = existing
      ? this.api.update(this.kind(), existing.id, request)
      : this.api.create(this.kind(), request);
    save.subscribe({
      next: (saved) => {
        this.snackBar.open(`${saved.name} was saved.`, undefined, { duration: 3000 });
        void this.router.navigateByUrl(this.listUrl());
      },
      error: (error) => {
        this.busy.set(false);
        this.error.set(applyServerErrors(this.form, error));
      },
    });
  }
}
