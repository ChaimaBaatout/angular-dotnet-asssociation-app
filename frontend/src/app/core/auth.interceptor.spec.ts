import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { SessionResponse } from './models';

const session = (accessToken: string): SessionResponse => ({
  accessToken,
  user: {
    id: 1,
    email: 'a@b.c',
    firstName: 'A',
    lastName: 'B',
    phoneNumber: null,
    profileName: 'P',
    permissions: [],
    mustChangePassword: false,
  },
});

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);

    auth.login('a@b.c', 'pw').subscribe();
    backend.expectOne('/api/auth/login').flush(session('old'));
  });

  afterEach(() => backend.verify());

  it('attaches the access token to API calls but not to other URLs', () => {
    http.get('/api/users').subscribe();
    http.get('https://example.com/data').subscribe();

    expect(backend.expectOne('/api/users').request.headers.get('Authorization')).toBe('Bearer old');
    expect(backend.expectOne('https://example.com/data').request.headers.has('Authorization')).toBe(false);
  });

  it('refreshes once for concurrent 401s and retries each request with the new token', () => {
    const results: unknown[] = [];
    http.get('/api/a').subscribe((r) => results.push(r));
    http.get('/api/b').subscribe((r) => results.push(r));

    backend.expectOne('/api/a').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/b').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/auth/refresh').flush(session('new'));

    const retryA = backend.expectOne('/api/a');
    const retryB = backend.expectOne('/api/b');
    expect(retryA.request.headers.get('Authorization')).toBe('Bearer new');
    expect(retryB.request.headers.get('Authorization')).toBe('Bearer new');
    retryA.flush('a');
    retryB.flush('b');
    expect(results).toEqual(['a', 'b']);
  });

  it('ends the session and reports the original error when refresh fails', () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    let status = 0;
    http.get('/api/a').subscribe({ error: (e) => (status = e.status) });

    backend.expectOne('/api/a').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(status).toBe(401);
    expect(auth.user()).toBeNull();
    expect(navigate).toHaveBeenCalledWith('/login');
  });

  it('does not refresh on a 403', () => {
    let status = 0;
    http.get('/api/a').subscribe({ error: (e) => (status = e.status) });

    backend.expectOne('/api/a').flush(null, { status: 403, statusText: 'Forbidden' });

    expect(status).toBe(403);
    backend.expectNone('/api/auth/refresh');
  });
});
