import { HttpErrorResponse } from '@angular/common/http';
import { FormGroup } from '@angular/forms';

export function errorMessage(error: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (!(error instanceof HttpErrorResponse)) return fallback;
  if (error.status === 0) return 'Cannot reach the server. Check your connection and try again.';
  if (error.status === 429) return 'Too many attempts. Please wait a minute and try again.';
  if (error.status === 403 && !error.error?.detail) return 'You do not have permission to do this.';
  return error.error?.detail ?? fallback;
}

/**
 * Shows validation errors from the API on the matching form controls.
 * Returns a message for anything that could not be attached to a control.
 */
export function applyServerErrors(form: FormGroup, error: unknown): string | null {
  const fieldErrors = error instanceof HttpErrorResponse ? error.error?.errors : null;
  if (!fieldErrors) return errorMessage(error);

  const unmatched: string[] = [];
  for (const [key, messages] of Object.entries<string[]>(fieldErrors)) {
    const control = form.get(key.charAt(0).toLowerCase() + key.slice(1));
    if (control) control.setErrors({ server: messages[0] });
    else unmatched.push(messages[0]);
  }
  return unmatched.length ? unmatched.join(' ') : null;
}
