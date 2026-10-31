import { Clipboard } from '@angular/cdk/clipboard';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';

export interface ConfirmDialogData {
  title: string;
  message: string;
  confirmLabel: string;
}

/** Closes with true when confirmed. */
@Component({
  selector: 'app-confirm-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>{{ data.title }}</h2>
    <mat-dialog-content>{{ data.message }}</mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton mat-dialog-close>Cancel</button>
      <button matButton="filled" [mat-dialog-close]="true">{{ data.confirmLabel }}</button>
    </mat-dialog-actions>
  `,
})
export class ConfirmDialog {
  protected readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);
}

export interface TemporaryPasswordDialogData {
  email: string;
  password: string;
}

@Component({
  selector: 'app-temporary-password-dialog',
  imports: [MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Temporary password</h2>
    <mat-dialog-content>
      <p>
        Give this password to <strong>{{ data.email }}</strong> over a secure channel. It will not be shown again, and
        they will be asked to replace it when they sign in.
      </p>
      <code class="password">{{ data.password }}</code>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button matButton (click)="copy()">{{ copied() ? 'Copied' : 'Copy' }}</button>
      <button matButton="filled" mat-dialog-close>Done</button>
    </mat-dialog-actions>
  `,
  styles: `
    .password {
      display: block;
      padding: 12px;
      border-radius: 8px;
      background: var(--mat-sys-surface-container-high);
      font-size: 1.1rem;
      overflow-wrap: anywhere;
    }
  `,
})
export class TemporaryPasswordDialog {
  private readonly clipboard = inject(Clipboard);

  protected readonly data = inject<TemporaryPasswordDialogData>(MAT_DIALOG_DATA);
  protected readonly copied = signal(false);

  protected copy(): void {
    this.copied.set(this.clipboard.copy(this.data.password));
  }
}
