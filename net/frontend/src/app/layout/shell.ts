import { Component, HostListener, inject, signal } from '@angular/core';
import { NavigationStart, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Auth } from '../core/auth';
import { Notifier } from '../core/notifier';

type MenuName = 'settings' | 'user' | null;

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  styles: `
    :host { display: block; }
    .topbar { position: sticky; top: 0; z-index: 20; background: #fff; border-bottom: 1px solid var(--border); box-shadow: 0 2px 10px rgba(15, 36, 89, .06); }
    .topbar::after { content: ""; display: block; height: 3px; background: var(--grad-hover); }
    .bar { display: flex; align-items: center; gap: 18px; max-width: 1180px; margin: 0 auto; padding: 8px 16px; }
    .brand { display: flex; align-items: center; gap: 12px; text-decoration: none; }
    .brand img { height: 34px; }
    .brand span { font: 600 15px var(--font-heading); color: var(--b); padding-left: 12px; border-left: 2px solid var(--g); white-space: nowrap; }
    nav { display: flex; align-items: center; gap: 4px; margin-left: auto; }
    .link {
      display: inline-flex; align-items: center; gap: 8px; height: 40px; padding: 0 14px; border: 0; border-radius: 999px;
      background: none; color: var(--text); font: 500 14px var(--font); cursor: pointer; text-decoration: none; white-space: nowrap;
    }
    .link:hover { background: var(--g-soft); text-decoration: none; }
    .link.active { background: var(--b); color: #fff; }
    .link i { color: var(--g); }
    .link.active i { color: #fff; }
    .menu { position: relative; }
    .dropdown {
      position: absolute; right: 0; top: calc(100% + 8px); min-width: 250px; padding: 8px; z-index: 30;
      border: 1px solid var(--border); border-top: 3px solid var(--g); border-radius: 8px 20px 8px 20px;
      background: #fff; box-shadow: 0 16px 36px rgba(15, 36, 89, .16);
    }
    .dropdown a, .dropdown button {
      display: flex; align-items: center; gap: 10px; width: 100%; padding: 10px 12px; border: 0; border-radius: 10px;
      background: none; color: var(--text); font: 400 14px var(--font); text-align: left; cursor: pointer; text-decoration: none;
    }
    .dropdown a:hover, .dropdown button:hover { background: var(--g-soft); text-decoration: none; }
    .dropdown a.active { background: var(--b-soft); color: var(--b); font-weight: 600; }
    .dropdown i { width: 18px; color: var(--g); text-align: center; }
    .dropdown .who { padding: 8px 12px 10px; font-size: 12px; color: var(--muted); border-bottom: 1px solid var(--border); margin-bottom: 6px; }
    .dropdown .who b { display: block; font-size: 14px; color: var(--text); }
    .burger { display: none; margin-left: auto; width: 42px; height: 42px; border: 1.5px solid var(--border); border-radius: 12px; background: #fff; color: var(--b); font-size: 18px; cursor: pointer; }
    .notices { max-width: 1180px; margin: 16px auto 0; padding: 0 16px; }
    .notices .msg { margin-bottom: 8px; }
    @media (max-width: 940px) {
      .burger { display: block; }
      nav { display: none; position: absolute; left: 0; right: 0; top: 100%; flex-direction: column; align-items: stretch;
            margin: 0; padding: 10px 16px 16px; background: #fff; border-bottom: 1px solid var(--border); }
      nav.open { display: flex; }
      .link { width: 100%; }
      .dropdown { position: static; box-shadow: none; margin-top: 6px; }
      .brand span { display: none; }
    }
  `,
  template: `
    <header class="topbar">
      <div class="bar">
        <a class="brand" routerLink="/">
          <img src="logo-bnaassurances.png" alt="BNA Assurances" />
          <span>Gestion de magasin</span>
        </a>
        <button class="burger" type="button" aria-label="Menu" (click)="navOpen.set(!navOpen())">
          <i class="fa-solid fa-bars"></i>
        </button>
        <nav [class.open]="navOpen()" aria-label="Navigation principale">
          <a class="link" routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }">
            <i class="fa-solid fa-gauge"></i> Tableau de bord
          </a>
          <a class="link" routerLink="/movements" routerLinkActive="active"><i class="fa-solid fa-right-left"></i> Mouvements</a>
          <a class="link" routerLink="/alerts" routerLinkActive="active"><i class="fa-solid fa-triangle-exclamation"></i> Alertes</a>
          <a class="link" routerLink="/inventory" routerLinkActive="active"><i class="fa-solid fa-clipboard-list"></i> Inventaire</a>

          <div class="menu">
            <button class="link" type="button" aria-haspopup="true" [attr.aria-expanded]="open() === 'settings'" (click)="toggle('settings')">
              <i class="fa-solid fa-gear"></i> Paramétrage <i class="fa-solid fa-chevron-down" style="font-size: 10px"></i>
            </button>
            @if (open() === 'settings') {
              <div class="dropdown" role="menu">
                <a routerLink="/settings/referentials" routerLinkActive="active" role="menuitem">
                  <i class="fa-solid fa-list-ul"></i> Fournisseurs, agents, services
                </a>
                @if (auth.isAdmin()) {
                  <a routerLink="/settings/users" routerLinkActive="active" role="menuitem"><i class="fa-solid fa-users"></i> Utilisateurs</a>
                  <a routerLink="/settings/audit" routerLinkActive="active" role="menuitem"><i class="fa-solid fa-book"></i> Audit</a>
                }
              </div>
            }
          </div>

          <div class="menu">
            <button class="link" type="button" aria-haspopup="true" [attr.aria-expanded]="open() === 'user'" (click)="toggle('user')">
              <i class="fa-solid fa-circle-user"></i> {{ auth.user()?.displayName }}
            </button>
            @if (open() === 'user') {
              <div class="dropdown" role="menu">
                <div class="who">
                  <b>{{ auth.user()?.displayName }}</b>
                  {{ auth.isAdmin() ? 'Administrateur' : 'Gestionnaire' }}
                </div>
                <a routerLink="/account/password" role="menuitem"><i class="fa-solid fa-key"></i> Changer le mot de passe</a>
                <button type="button" role="menuitem" (click)="logout()"><i class="fa-solid fa-right-from-bracket"></i> Déconnexion</button>
              </div>
            }
          </div>
        </nav>
      </div>
    </header>

    @if (notifier.notices().length) {
      <div class="notices" role="status" aria-live="polite">
        @for (notice of notifier.notices(); track notice.id) {
          <div class="msg" [class]="'msg msg-' + notice.kind">
            <i class="fa-solid" [class]="notice.kind === 'ok' ? 'fa-circle-check' : notice.kind === 'error' ? 'fa-circle-exclamation' : 'fa-triangle-exclamation'"></i>
            <span>{{ notice.text }}</span>
            <button class="close" type="button" aria-label="Fermer" (click)="notifier.dismiss(notice.id)">×</button>
          </div>
        }
      </div>
    }

    <main class="page"><router-outlet /></main>
  `,
})
export class Shell {
  protected readonly auth = inject(Auth);
  protected readonly notifier = inject(Notifier);
  protected readonly open = signal<MenuName>(null);
  protected readonly navOpen = signal(false);

  constructor() {
    inject(Router).events.subscribe((event) => {
      if (event instanceof NavigationStart) {
        this.open.set(null);
        this.navOpen.set(false);
        this.notifier.clearErrors();
      }
    });
  }

  protected toggle(menu: Exclude<MenuName, null>): void {
    this.open.update((current) => (current === menu ? null : menu));
  }

  protected logout(): Promise<void> {
    return this.auth.logout();
  }

  @HostListener('document:click', ['$event'])
  protected closeOnOutsideClick(event: MouseEvent): void {
    if (!(event.target as HTMLElement).closest('.menu')) this.open.set(null);
  }

  @HostListener('document:keydown.escape')
  protected closeOnEscape(): void {
    this.open.set(null);
  }
}
