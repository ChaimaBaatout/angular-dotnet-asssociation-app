import { BreakpointObserver } from '@angular/cdk/layout';
import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../core/auth.service';
import { Permissions } from '../core/models';

@Component({
  selector: 'app-shell',
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatSidenavModule,
    MatToolbarModule,
    MatListModule,
    MatIconModule,
    MatButtonModule,
    MatMenuModule,
  ],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  protected readonly auth = inject(AuthService);

  protected readonly isCompact = toSignal(
    inject(BreakpointObserver)
      .observe('(max-width: 959.98px)')
      .pipe(map((state) => state.matches)),
    { initialValue: false },
  );

  protected readonly navItems = computed(() => {
    const items = [{ label: 'Home', icon: 'home', link: '/', exact: true }];
    if (this.auth.user()?.permissions.includes(Permissions.usersRead)) {
      items.push({ label: 'Users', icon: 'group', link: '/admin/users', exact: false });
    }
    if (this.auth.user()?.permissions.includes(Permissions.profilesRead)) {
      items.push({ label: 'Profiles', icon: 'admin_panel_settings', link: '/admin/profiles', exact: false });
    }
    return items;
  });
}
