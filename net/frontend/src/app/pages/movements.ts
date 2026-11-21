import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Api, errorMessage } from '../core/api';
import { Movement, MovementFilters, Page, Product, Reference, ReferenceSection } from '../core/models';
import { Notifier } from '../core/notifier';
import { Hero } from '../shared/hero';
import { Pager } from '../shared/pager';

const EMPTY_FILTERS: MovementFilters = {
  productId: '', dateFrom: '', dateTo: '', movementType: '', agentId: '', supplierId: '', serviceId: '',
};

@Component({
  selector: 'app-movements',
  imports: [FormsModule, RouterLink, DecimalPipe, DatePipe, Hero, Pager],
  template: `
    <app-hero eyebrow="Historique" title="Recherche de mouvements" lead="Filtrez par produit, période, type, agent, fournisseur ou service.">
      <button class="btn btn-light" type="button" (click)="download('pdf')"><i class="fa-solid fa-file-pdf"></i> PDF</button>
      <button class="btn btn-light" type="button" (click)="download('excel')"><i class="fa-solid fa-file-excel"></i> Excel</button>
    </app-hero>

    <div class="card">
      <form (ngSubmit)="search()">
        <div class="grid grid-4">
          <div class="field">
            <label for="productId">Produit</label>
            <select id="productId" name="productId" class="input" [(ngModel)]="filters.productId">
              <option value="">Tous</option>
              @for (p of products(); track p.id) { <option [value]="p.id">{{ p.name }} ({{ p.sku }})</option> }
            </select>
          </div>
          <div class="field">
            <label for="dateFrom">Date début</label>
            <input id="dateFrom" name="dateFrom" type="date" class="input" [(ngModel)]="filters.dateFrom" />
          </div>
          <div class="field">
            <label for="dateTo">Date fin</label>
            <input id="dateTo" name="dateTo" type="date" class="input" [(ngModel)]="filters.dateTo" />
          </div>
          <div class="field">
            <label for="movementType">Type</label>
            <select id="movementType" name="movementType" class="input" [(ngModel)]="filters.movementType">
              <option value="">Tous</option>
              <option value="IN">Entrée</option>
              <option value="OUT">Sortie</option>
            </select>
          </div>
          <div class="field">
            <label for="agentId">Agent</label>
            <select id="agentId" name="agentId" class="input" [(ngModel)]="filters.agentId">
              <option value="">Tous</option>
              @for (a of agents(); track a.id) { <option [value]="a.id">{{ a.name }}</option> }
            </select>
          </div>
          <div class="field">
            <label for="supplierId">Fournisseur</label>
            <select id="supplierId" name="supplierId" class="input" [(ngModel)]="filters.supplierId">
              <option value="">Tous</option>
              @for (s of suppliers(); track s.id) { <option [value]="s.id">{{ s.name }}</option> }
            </select>
          </div>
          <div class="field">
            <label for="serviceId">Service</label>
            <select id="serviceId" name="serviceId" class="input" [(ngModel)]="filters.serviceId">
              <option value="">Tous</option>
              @for (s of services(); track s.id) { <option [value]="s.id">{{ s.name }}</option> }
            </select>
          </div>
        </div>
        <div class="actions-bar" style="justify-content: flex-end">
          <button class="btn btn-ghost" type="button" (click)="reset()"><i class="fa-solid fa-rotate-left"></i> Réinitialiser</button>
          <button class="btn btn-primary" type="submit"><i class="fa-solid fa-magnifying-glass"></i> Rechercher</button>
        </div>
      </form>
    </div>

    @if (error()) {
      <div class="msg msg-error" style="margin-top: 20px"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div>
    }

    <div class="card flat" style="margin-top: 20px">
      @if (result(); as r) {
        @if (r.items.length) {
          <div class="table-wrap">
            <table class="table">
              <thead>
                <tr>
                  <th>Produit</th><th>Type</th><th class="num">Quantité</th><th class="num">Prix HT</th>
                  <th class="num">Taxe</th><th class="num">Total TTC</th>
                  <th>Fournisseur</th><th>Agent</th><th>Service</th><th>Motif</th><th>Par</th><th>Date</th>
                </tr>
              </thead>
              <tbody>
                @for (m of r.items; track m.id) {
                  <tr>
                    <td><a [routerLink]="['/products', m.productId]">{{ m.productName }}</a><br /><span class="muted small">{{ m.productSku }}</span></td>
                    <td><span class="badge" [class]="'badge ' + (m.movementType === 'IN' ? 'badge-in' : 'badge-out')">{{ m.movementType === 'IN' ? 'Entrée' : 'Sortie' }}</span></td>
                    <td [class]="m.movementType === 'IN' ? 'num qty-in' : 'num qty-out'">{{ m.quantityChange > 0 ? '+' : '' }}{{ m.quantityChange }}</td>
                    <td class="num">{{ m.unitPrice | number: '1.2-2' }}<br /><span class="muted small">HT : {{ m.totalHt | number: '1.2-2' }}</span></td>
                    <td class="num">{{ m.taxAmount | number: '1.2-2' }}<br /><span class="muted small">{{ m.taxRate | number: '1.0-2' }} %</span></td>
                    <td class="num"><b>{{ m.totalTtc | number: '1.2-2' }}</b></td>
                    <td>{{ m.supplierName || '-' }}</td>
                    <td>{{ m.agentName || '-' }}</td>
                    <td>
                      {{ m.serviceName || '-' }}
                      @if (!m.supplierName && !m.agentName && !m.serviceName && m.legacyText) {
                        <br /><span class="muted small" title="Ancienne saisie">{{ m.legacyText }}</span>
                      }
                    </td>
                    <td>{{ m.reason || '-' }}</td>
                    <td class="small">{{ m.recordedBy }}</td>
                    <td class="nowrap small">{{ m.timestamp | date: 'dd/MM/yyyy HH:mm' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <p class="muted small" style="margin: 0; padding: 12px 20px">{{ r.total }} mouvement(s) · page {{ r.page }} / {{ r.pages }}</p>
        } @else {
          <div class="empty"><i class="fa-solid fa-inbox"></i> Aucun mouvement ne correspond aux critères sélectionnés.</div>
        }
      } @else if (!error()) {
        <div class="empty"><i class="fa-solid fa-spinner fa-spin"></i> Chargement…</div>
      }
    </div>

    @if (result(); as r) {
      <app-pager [page]="r.page" [pages]="r.pages" (pageChange)="goTo($event)" />
    }
  `,
})
export class Movements {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly notifier = inject(Notifier);

  protected filters: MovementFilters = { ...EMPTY_FILTERS };
  protected readonly result = signal<Page<Movement> | null>(null);
  protected readonly error = signal('');
  protected readonly products = signal<Product[]>([]);
  protected readonly agents = signal<Reference[]>([]);
  protected readonly suppliers = signal<Reference[]>([]);
  protected readonly services = signal<Reference[]>([]);

  constructor() {
    void this.loadLists();
    // L'état de la recherche (filtres + page) vit dans l'URL : rechargement et retour arrière fonctionnent.
    this.route.queryParamMap.subscribe((params) => {
      const next: MovementFilters = { ...EMPTY_FILTERS };
      for (const key of Object.keys(next) as (keyof MovementFilters)[]) next[key] = params.get(key) ?? '';
      this.filters = next;
      void this.load(Number(params.get('page')) || 1);
    });
  }

  private async loadLists(): Promise<void> {
    try {
      const [products, sections] = await Promise.all([
        this.api.get<Product[]>('/api/products'),
        this.api.get<ReferenceSection[]>('/api/referentials'),
      ]);
      this.products.set(products);
      const items = (kind: string) => sections.find((s) => s.kind === kind)?.items ?? [];
      this.agents.set(items('agent'));
      this.suppliers.set(items('supplier'));
      this.services.set(items('service'));
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }

  private async load(page: number): Promise<void> {
    this.error.set('');
    try {
      this.result.set(await this.api.get<Page<Movement>>('/api/movements', { ...this.filters, page }));
    } catch (error) {
      this.result.set(null);
      this.error.set(errorMessage(error));
    }
  }

  private navigate(page: number): void {
    const queryParams: Record<string, string | number> = {};
    for (const [key, value] of Object.entries(this.filters)) if (value) queryParams[key] = value;
    if (page > 1) queryParams['page'] = page;
    void this.router.navigate([], { relativeTo: this.route, queryParams });
  }

  protected search(): void { this.navigate(1); }
  protected goTo(page: number): void { this.navigate(page); }

  protected reset(): void {
    this.filters = { ...EMPTY_FILTERS };
    this.navigate(1);
  }

  protected async download(format: 'pdf' | 'excel'): Promise<void> {
    try {
      await this.api.download(`/api/export/movements/${format}`, { ...this.filters });
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }
}
