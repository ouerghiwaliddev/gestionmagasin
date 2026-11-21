import { DatePipe, JsonPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Api, errorMessage } from '../core/api';
import { AuditLog, Page } from '../core/models';
import { Notifier } from '../core/notifier';
import { Hero } from '../shared/hero';
import { Pager } from '../shared/pager';

interface AuditResponse {
  logs: Page<AuditLog>;
  users: { id: number; username: string }[];
  actions: string[];
}

@Component({
  selector: 'app-audit',
  imports: [FormsModule, DatePipe, JsonPipe, Hero, Pager],
  template: `
    <app-hero eyebrow="Paramétrage" title="Journal d'audit" lead="Historique non modifiable des actions effectuées dans l'application." />

    <div class="card">
      <form (ngSubmit)="search()">
        <div class="grid grid-4">
          <div class="field"><label for="actorId">Utilisateur</label>
            <select id="actorId" name="actorId" class="input" [(ngModel)]="filters.actorId">
              <option value="">Tous</option>
              @for (u of data()?.users ?? []; track u.id) { <option [value]="u.id">{{ u.username }}</option> }
            </select></div>
          <div class="field"><label for="action">Action</label>
            <select id="action" name="action" class="input" [(ngModel)]="filters.action">
              <option value="">Toutes</option>
              @for (a of data()?.actions ?? []; track a) { <option [value]="a">{{ a }}</option> }
            </select></div>
          <div class="field"><label for="dateFrom">Date début</label>
            <input id="dateFrom" name="dateFrom" type="date" class="input" [(ngModel)]="filters.dateFrom" /></div>
          <div class="field"><label for="dateTo">Date fin</label>
            <input id="dateTo" name="dateTo" type="date" class="input" [(ngModel)]="filters.dateTo" /></div>
        </div>
        <div class="actions-bar" style="justify-content: flex-end">
          <button class="btn btn-ghost" type="button" (click)="reset()"><i class="fa-solid fa-rotate-left"></i> Réinitialiser</button>
          <button class="btn btn-primary" type="submit"><i class="fa-solid fa-magnifying-glass"></i> Filtrer</button>
        </div>
      </form>
    </div>

    @if (error()) {
      <div class="msg msg-error" style="margin-top: 20px"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div>
    }

    @if (data(); as d) {
      <div class="card flat" style="margin-top: 20px">
        @if (d.logs.items.length) {
          <div class="table-wrap">
            <table class="table">
              <thead><tr><th>Date</th><th>Utilisateur</th><th>Action</th><th>Cible</th><th>Détails</th></tr></thead>
              <tbody>
                @for (log of d.logs.items; track log.id) {
                  <tr>
                    <td class="nowrap small">{{ log.createdAt | date: 'dd/MM/yyyy HH:mm:ss' }}</td>
                    <td>{{ log.actorUsername }}</td>
                    <td><span class="badge badge-muted">{{ log.action }}</span></td>
                    <td class="small">{{ log.targetType }}{{ log.targetId ? ' #' + log.targetId : '' }}</td>
                    <td>
                      <details>
                        <summary class="small" style="cursor: pointer; color: var(--b)">Voir</summary>
                        <pre class="small" style="margin: 8px 0 0; max-width: 520px; white-space: pre-wrap">{{ log.details | json }}</pre>
                      </details>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <p class="muted small" style="margin: 0; padding: 12px 20px">{{ d.logs.total }} entrée(s) · page {{ d.logs.page }} / {{ d.logs.pages }}</p>
        } @else {
          <div class="empty"><i class="fa-solid fa-inbox"></i> Aucune entrée ne correspond aux filtres.</div>
        }
      </div>
      <app-pager [page]="d.logs.page" [pages]="d.logs.pages" (pageChange)="navigate($event)" />
    }
  `,
})
export class AuditPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly notifier = inject(Notifier);

  protected filters = { actorId: '', action: '', dateFrom: '', dateTo: '' };
  protected readonly data = signal<AuditResponse | null>(null);
  protected readonly error = signal('');

  constructor() {
    this.route.queryParamMap.subscribe((params) => {
      this.filters = {
        actorId: params.get('actorId') ?? '', action: params.get('action') ?? '',
        dateFrom: params.get('dateFrom') ?? '', dateTo: params.get('dateTo') ?? '',
      };
      void this.load(Number(params.get('page')) || 1);
    });
  }

  private async load(page: number): Promise<void> {
    this.error.set('');
    try {
      this.data.set(await this.api.get<AuditResponse>('/api/admin/audit', { ...this.filters, page }));
    } catch (error) {
      this.error.set(errorMessage(error));
      this.notifier.clearErrors();
    }
  }

  protected navigate(page: number): void {
    const queryParams: Record<string, string | number> = {};
    for (const [key, value] of Object.entries(this.filters)) if (value) queryParams[key] = value;
    if (page > 1) queryParams['page'] = page;
    void this.router.navigate([], { relativeTo: this.route, queryParams });
  }

  protected search(): void { this.navigate(1); }

  protected reset(): void {
    this.filters = { actorId: '', action: '', dateFrom: '', dateTo: '' };
    this.navigate(1);
  }
}
