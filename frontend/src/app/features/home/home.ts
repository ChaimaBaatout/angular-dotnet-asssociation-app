import { Component, inject } from '@angular/core';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-home',
  template: `
    <header class="page-header">
      <h1>Welcome, {{ auth.user()?.firstName }}</h1>
    </header>
    <section class="panel">
      <p>You are signed in with the <strong>{{ auth.user()?.profileName }}</strong> profile.</p>
    </section>
  `,
})
export class Home {
  protected readonly auth = inject(AuthService);
}
