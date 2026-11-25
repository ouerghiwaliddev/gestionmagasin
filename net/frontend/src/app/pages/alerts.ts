import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, errorMessage } from '../core/api';
import { Auth } from '../core/auth';
import { Alert } from '../core/models';
import { Notifier } from '../core/notifier';
import { Hero } from '../shared/hero';

@Component({
  selector: 'app-alerts',
  imports: [RouterLink, DatePipe, Hero],
  template: `
    <app-hero eyebrow="Suivi" title="Alertes de stock" lead="Ruptures et stocks sous le seuil d'alerte.">
      <button class="btn btn-light" type="button" (click)="download('pdf')"><i class="fa-solid fa-file-pdf"></i> PDF</button>
      <button class="btn btn-light" type="button" (click)="download('excel')"><i class="fa-solid fa-file-excel"></i> Excel</button>
    </app-hero>

    @if (data(); as d) {
      <div class="card">
        <h2>Alertes actives ({{ d.active.length }})</h2>
        @if (d.active.length) {
          <div class="stack" style="margin-top: 16px">
            @for (alert of d.active; track alert.id) {
              <div class="msg" [class]="'msg ' + (alert.alertType === 'CRITICAL' ? 'msg-error' : 'msg-warn')" style="margin: 0">
                <div class="grow">
                  {{ alert.message }}<br />
                  <span class="small">{{ alert.createdAt | date: 'dd/MM/yyyy HH:mm' }} ·
                    <a [routerLink]="['/products', alert.productId]">{{ alert.productName }}</a></span>
                </div>
                @if (auth.canWrite()) { <button class="btn btn-ghost sm" type="button" (click)="resolve(alert)"><i class="fa-solid fa-check"></i> Résoudre</button> }
              </div>
            }
          </div>
        } @else {
          <div class="empty"><i class="fa-solid fa-circle-check" style="color: var(--g)"></i> Aucune alerte active : tous les stocks sont suffisants.</div>
        }
      </div>

      @if (d.resolved.length) {
        <div class="card">
          <h2>Dernières alertes résolues</h2>
          <div class="stack" style="margin-top: 16px">
            @for (alert of d.resolved; track alert.id) {
              <div class="msg msg-ok" style="margin: 0">
                <span class="grow">{{ alert.message }}</span>
                <span class="small nowrap">{{ alert.createdAt | date: 'dd/MM/yyyy HH:mm' }}</span>
              </div>
            }
          </div>
        </div>
      }
    } @else {
      <div class="empty"><i class="fa-solid fa-spinner fa-spin"></i> Chargement…</div>
    }
  `,
})
export class Alerts {
  private readonly api = inject(Api);
  protected readonly auth = inject(Auth);
  private readonly notifier = inject(Notifier);
  protected readonly data = signal<{ active: Alert[]; resolved: Alert[] } | null>(null);

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.data.set(await this.api.get('/api/alerts'));
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }

  protected async resolve(alert: Alert): Promise<void> {
    try {
      await this.api.post(`/api/alerts/${alert.id}/resolve`);
      this.notifier.ok('Alerte marquée comme résolue.');
      await this.load();
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }

  protected async download(format: 'pdf' | 'excel'): Promise<void> {
    try {
      await this.api.download(`/api/export/alerts/${format}`);
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }
}
