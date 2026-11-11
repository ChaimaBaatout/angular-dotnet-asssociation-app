import { DecimalPipe } from '@angular/common';
import { Component, inject, OnInit, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { RouterLink } from '@angular/router';
import { ArticlesApi } from '../../core/api';
import { AuthService } from '../../core/auth.service';
import { ArticleStats, CURRENCY, Permissions } from '../../core/models';

@Component({
  selector: 'app-home',
  imports: [DecimalPipe, RouterLink, MatIconModule],
  template: `
    <header class="page-header">
      <h1>Welcome, {{ auth.user()?.firstName }}</h1>
    </header>

    @if (stats(); as s) {
      <section class="stats" aria-label="Stock">
        <a class="stat panel" routerLink="/articles">
          <mat-icon>inventory_2</mat-icon>
          <span class="value">{{ s.total | number }}</span>
          <span class="label">Active articles</span>
        </a>
        <a class="stat panel" routerLink="/articles" [queryParams]="{ lowStock: true }" [class.alert]="s.lowStock > 0">
          <mat-icon>warning</mat-icon>
          <span class="value">{{ s.lowStock | number }}</span>
          <span class="label">Low or out of stock{{ s.outOfStock ? ' (' + s.outOfStock + ' out)' : '' }}</span>
        </a>
        <div class="stat panel">
          <mat-icon>payments</mat-icon>
          <span class="value">{{ s.stockValue | number: '1.3-3' }} {{ currency }}</span>
          <span class="label">Stock value at purchase price</span>
        </div>
      </section>
    }

    <section class="panel">
      <p>You are signed in with the <strong>{{ auth.user()?.profileName }}</strong> profile.</p>
    </section>
  `,
  styles: `
    .stats {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 220px), 1fr));
      gap: 16px;
    }

    .stat {
      display: grid;
      gap: 4px;
      color: inherit;
      text-decoration: none;

      mat-icon {
        color: var(--mat-sys-primary);
      }

      &.alert mat-icon {
        color: #ffcf66;
      }
    }

    a.stat:hover {
      border-color: var(--mat-sys-primary);
    }

    .value {
      font: var(--mat-sys-headline-small);
      overflow-wrap: anywhere;
    }

    .label {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
})
export class Home implements OnInit {
  private readonly articles = inject(ArticlesApi);
  protected readonly auth = inject(AuthService);
  protected readonly currency = CURRENCY;
  protected readonly stats = signal<ArticleStats | null>(null);

  ngOnInit(): void {
    if (this.auth.can(Permissions.articlesRead)) {
      this.articles.stats().subscribe((stats) => this.stats.set(stats));
    }
  }
}
