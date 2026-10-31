import { qrSignInUrl, tokenFromFragment, tokenFromQrText } from './link-token';

const token = 'a1'.repeat(32);

describe('link tokens', () => {
  it('reads a token from a URL fragment', () => {
    expect(tokenFromFragment(`token=${token}`)).toBe(token);
  });

  it('rejects missing and malformed fragments', () => {
    expect(tokenFromFragment(null)).toBeNull();
    expect(tokenFromFragment('')).toBeNull();
    expect(tokenFromFragment('token=short')).toBeNull();
    expect(tokenFromFragment(`other=${token}`)).toBeNull();
  });

  it('round trips through a QR code URL', () => {
    expect(tokenFromQrText(qrSignInUrl('https://app.example', token))).toBe(token);
  });

  it('ignores QR codes that are not sign-in codes', () => {
    expect(tokenFromQrText('hello')).toBeNull();
    expect(tokenFromQrText('https://example.com/some/page')).toBeNull();
  });
});
