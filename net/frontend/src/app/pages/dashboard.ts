import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, errorMessage } from '../core/api';
import { Auth } from '../core/auth';
import { Dashboard as DashboardData, PERIODS } from '../core/models';
import { Notifier } from '../core/notifier';
import { Hero } from '../shared/hero';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, FormsModule, DecimalPipe, DatePipe, Hero],
  template: `
    <app-hero eyebrow="Gestion de magasin" title="Tableau de bord"
              [lead]="'Bonjour ' + (auth.user()?.displayName ?? '') + ' : état des stocks et activité de la période.'">
      <button class="btn btn-light" type="button" (click)="download('pdf')"><i class="fa-solid fa-file-pdf"></i> PDF</button>
      <button class="btn btn-light" type="button" (click)="download('excel')"><i class="fa-solid fa-file-excel"></i> Excel</button>
      <a class="btn btn-success" routerLink="/products/new"><i class="fa-solid fa-plus"></i> Nouveau produit</a>
    </app-hero>

    @if (data(); as d) {
      <div class="row" style="margin-bottom: 16px">
        <span class="muted small">Période d'activité :</span>
        <div class="pills" role="group" aria-label="Période">
          @for (p of periods; track p.key) {
            <button class="pill" type="button" [class.active]="d.stats.period === p.key" (click)="setPeriod(p.key)">{{ p.label }}</button>
          }
        </div>
      </div>

      <div class="kpis">
        <div class="kpi main"><b>{{ d.stats.totalProducts }}</b><span>Produits en stock</span></div>
        <div class="kpi danger"><b>{{ d.stats.criticalProducts }}</b><span>Ruptures de stock</span></div>
        <div class="kpi warn"><b>{{ d.stats.lowStockProducts }}</b><span>Stocks faibles</span></div>
        <div class="kpi"><b>{{ d.stats.activeAlerts }}</b><span>Alertes actives</span></div>
      </div>
      <div class="kpis" style="margin-top: 14px">
        <div class="kpi"><b>{{ d.stats.recentMovements }}</b><span>Mouvements ({{ d.stats.periodLabel }})</span></div>
        <div class="kpi">
          <b>{{ d.stats.entryQuantity }}</b>
          <span>Entrées : {{ d.stats.entryCount }} mvt · {{ d.stats.entryValue | number: '1.2-2' }} TND HT</span>
        </div>
        <div class="kpi">
          <b>{{ d.stats.exitQuantity }}</b>
          <span>Sorties : {{ d.stats.exitCount }} mvt · {{ d.stats.exitValue | number: '1.2-2' }} TND HT</span>
        </div>
      </div>

      @if (d.alerts.length) {
        <div class="card" style="margin-top: 24px">
          <h2>Alertes actives</h2>
          <div class="stack" style="margin-top: 14px">
            @for (alert of d.alerts; track alert.id) {
              <div class="msg" [class]="'msg ' + (alert.alertType === 'CRITICAL' ? 'msg-error' : 'msg-warn')" style="margin: 0">
                <span class="grow">{{ alert.message }}</span>
                <a [routerLink]="['/products', alert.productId]">Voir le produit</a>
              </div>
            }
          </div>
        </div>
      }

      <div class="card flat" style="margin-top: 24px">
        <div class="row" style="padding: 22px 24px 8px; justify-content: space-between">
          <h2 style="margin: 0">Produits</h2>
          <input class="input sm" style="max-width: 280px" type="search" placeholder="Rechercher (nom ou SKU)…"
                 aria-label="Rechercher un produit" [(ngModel)]="search" />
        </div>
        @if (visible().length) {
          <div class="table-wrap">
            <table class="table">
              <thead>
                <tr>
                  <th>Produit</th><th class="num">Stock</th><th class="num">Seuil</th><th>État</th><th class="num">Mis à jour</th><th></th>
                </tr>
              </thead>
              <tbody>
                @for (p of visible(); track p.id) {
                  <tr>
                    <td>
                      <a [routerLink]="['/products', p.id]">{{ p.name }}</a><br />
                      <span class="muted small">{{ p.sku }}</span>
                    </td>
                    <td class="num"><b>{{ p.currentStock }}</b></td>
                    <td class="num">{{ p.threshold }}</td>
                    <td>
                      <span class="badge" [class]="'badge ' + (p.isCritical ? 'badge-danger' : p.isLow ? 'badge-warn' : 'badge-ok')">{{ p.status }}</span>
                    </td>
                    <td class="num muted small">{{ p.updatedAt | date: 'dd/MM/yyyy HH:mm' }}</td>
                    <td class="right nowrap">
                      <a class="btn btn-ghost sm" [routerLink]="['/products', p.id, 'movement']"><i class="fa-solid fa-right-left"></i> Mouvement</a>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        } @else {
          <div class="empty">
            <i class="fa-solid fa-box-open"></i>
            {{ data()?.products?.length ? 'Aucun produit ne correspond à la recherche.' : 'Aucun produit pour le moment.' }}
          </div>
        }
      </div>
    } @else {
      <div class="empty"><i class="fa-solid fa-spinner fa-spin"></i> Chargement…</div>
    }
  `,
})
export class Dashboard {
  private readonly api = inject(Api);
  private readonly notifier = inject(Notifier);
  protected readonly auth = inject(Auth);
  protected readonly periods = PERIODS;
  protected readonly data = signal<DashboardData | null>(null);
  protected readonly searchText = signal('');

  protected get search(): string { return this.searchText(); }
  protected set search(value: string) { this.searchText.set(value ?? ''); }

  protected readonly visible = computed(() => {
    const term = this.searchText().trim().toLowerCase();
    const products = this.data()?.products ?? [];
    return term ? products.filter((p) => p.name.toLowerCase().includes(term) || p.sku.toLowerCase().includes(term)) : products;
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.data.set(await this.api.get<DashboardData>('/api/dashboard'));
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }

  protected async setPeriod(period: string): Promise<void> {
    try {
      await this.api.put('/api/dashboard/period', { period });
      await this.load();
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }

  protected async download(format: 'pdf' | 'excel'): Promise<void> {
    try {
      await this.api.download(`/api/export/dashboard/${format}`);
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }
}
