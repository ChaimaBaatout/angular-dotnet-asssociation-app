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

interface NavItem {
  label: string;
  icon: string;
  link: string;
  exact: boolean;
}

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

  /** Menu sections, each showing only the pages the user may open. Empty sections are left out. */
  protected readonly navSections = computed(() => {
    const allowed = (items: (NavItem & { permission: string })[]) =>
      items.filter((item) => this.auth.can(item.permission));
    return [
      { title: null, items: [{ label: 'Home', icon: 'home', link: '/', exact: true }] },
      {
        title: 'Business',
        items: allowed([
          { label: 'Orders', icon: 'receipt_long', link: '/orders', exact: false, permission: Permissions.ordersRead },
          { label: 'Articles', icon: 'inventory_2', link: '/articles', exact: false, permission: Permissions.articlesRead },
          { label: 'Suppliers', icon: 'local_shipping', link: '/suppliers', exact: false, permission: Permissions.suppliersRead },
          { label: 'Customers', icon: 'storefront', link: '/customers', exact: false, permission: Permissions.customersRead },
        ]),
      },
      {
        title: 'Administration',
        items: allowed([
          { label: 'Users', icon: 'group', link: '/admin/users', exact: false, permission: Permissions.usersRead },
          {
            label: 'Profiles',
            icon: 'admin_panel_settings',
            link: '/admin/profiles',
            exact: false,
            permission: Permissions.profilesRead,
          },
        ]),
      },
    ].filter((section) => section.items.length > 0);
  });
}
