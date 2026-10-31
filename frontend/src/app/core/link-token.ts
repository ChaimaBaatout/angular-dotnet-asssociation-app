const TOKEN = /^token=([0-9a-f]{64})$/i;

/**
 * Sign-in and password links carry their secret after the #, so it is never sent to
 * a server in the URL or recorded in access logs.
 */
export function tokenFromFragment(fragment: string | null | undefined): string | null {
  return fragment?.match(TOKEN)?.[1] ?? null;
}

/** Reads the token from the text of a scanned QR code. The scanned URL itself is never opened. */
export function tokenFromQrText(text: string): string | null {
  try {
    return tokenFromFragment(new URL(text).hash.slice(1));
  } catch {
    return null;
  }
}

export function qrSignInUrl(origin: string, token: string): string {
  return `${origin}/qr-login#token=${token}`;
}
