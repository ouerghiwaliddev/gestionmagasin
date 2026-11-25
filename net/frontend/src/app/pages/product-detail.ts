import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Api, errorMessage } from '../core/api';
import { Auth } from '../core/auth';
import { Alert, Movement, Product } from '../core/models';
import { Notifier } from '../core/notifier';
import { Hero } from '../shared/hero';

interface ProductView {
  product: Product;
  movements: Movement[];
  alerts: Alert[];
}

@Component({
  selector: 'app-product-detail',
  imports: [RouterLink, DecimalPipe, DatePipe, Hero],
  template: `
    @if (view(); as v) {
      <app-hero eyebrow="Produit" [title]="v.product.name" [lead]="'SKU ' + v.product.sku + (v.product.description ? ' · ' + v.product.description : '')">
        @if (auth.canWrite()) { <a class="btn btn-success" [routerLink]="['/products', v.product.id, 'movement']"><i class="fa-solid fa-right-left"></i> Mouvement</a>
        <a class="btn btn-light" [routerLink]="['/products', v.product.id, 'edit']"><i class="fa-solid fa-pen"></i> Modifier</a> }
        <button class="btn btn-light" type="button" (click)="download('pdf')"><i class="fa-solid fa-file-pdf"></i> PDF</button>
        <button class="btn btn-light" type="button" (click)="download('excel')"><i class="fa-solid fa-file-excel"></i> Excel</button>
      </app-hero>

      <div class="kpis">
        <div class="kpi" [class.danger]="v.product.isCritical" [class.warn]="v.product.isLow">
          <b>{{ v.product.currentStock }}</b><span>Stock actuel</span>
        </div>
        <div class="kpi"><b>{{ v.product.threshold }}</b><span>Seuil d'alerte</span></div>
        <div class="kpi">
          <span class="badge" [class]="'badge ' + (v.product.isCritical ? 'badge-danger' : v.product.isLow ? 'badge-warn' : 'badge-ok')">{{ v.product.status }}</span>
          <span>État du stock</span>
        </div>
        <div class="kpi"><b style="font-size: 15px">{{ v.product.updatedAt | date: 'dd/MM/yyyy HH:mm' }}</b><span>Dernière mise à jour</span></div>
      </div>

      @if (v.alerts.length) {
        <div class="card" style="margin-top: 24px">
          <h2>Alertes</h2>
          <div class="stack" style="margin-top: 14px">
            @for (alert of v.alerts; track alert.id) {
              <div class="msg" [class]="'msg ' + (alert.isResolved ? 'msg-ok' : alert.alertType === 'CRITICAL' ? 'msg-error' : 'msg-warn')" style="margin: 0">
                <span class="grow">{{ alert.message }}</span>
                <span class="small nowrap">{{ alert.isResolved ? 'Résolue · ' : '' }}{{ alert.createdAt | date: 'dd/MM/yyyy HH:mm' }}</span>
              </div>
            }
          </div>
        </div>
      }

      <div class="card flat" style="margin-top: 24px">
        <div style="padding: 22px 24px 8px">
          <h2 style="margin: 0">Derniers mouvements</h2>
          <p class="sub" style="margin: 6px 0 0">Les 20 plus récents. <a routerLink="/movements" [queryParams]="{ productId: v.product.id }">Voir tout l'historique</a></p>
        </div>
        @if (v.movements.length) {
          <div class="table-wrap">
            <table class="table">
              <thead>
                <tr>
                  <th>Date</th><th>Type</th><th class="num">Quantité</th><th class="num">Prix HT</th>
                  <th class="num">Taxe</th><th class="num">Total TTC</th><th>Contrepartie</th><th>Motif</th><th>Par</th>
                </tr>
              </thead>
              <tbody>
                @for (m of v.movements; track m.id) {
                  <tr>
                    <td class="nowrap small">{{ m.timestamp | date: 'dd/MM/yyyy HH:mm' }}</td>
                    <td><span class="badge" [class]="'badge ' + (m.movementType === 'IN' ? 'badge-in' : 'badge-out')">{{ m.movementType === 'IN' ? 'Entrée' : 'Sortie' }}</span></td>
                    <td class="num" [class]="m.movementType === 'IN' ? 'num qty-in' : 'num qty-out'">{{ m.quantityChange > 0 ? '+' : '' }}{{ m.quantityChange }}</td>
                    <td class="num">{{ m.unitPrice | number: '1.2-2' }}</td>
                    <td class="num">{{ m.taxAmount | number: '1.2-2' }}<br /><span class="muted small">{{ m.taxRate | number: '1.0-2' }} %</span></td>
                    <td class="num"><b>{{ m.totalTtc | number: '1.2-2' }}</b></td>
                    <td>
                      @if (m.supplierName) { <i class="fa-solid fa-truck muted"></i> {{ m.supplierName }} }
                      @else if (m.agentName || m.serviceName) { <i class="fa-solid fa-user muted"></i> {{ m.agentName || m.serviceName }} }
                      @else { <span class="muted small">{{ m.legacyText || '-' }}</span> }
                    </td>
                    <td>{{ m.reason || '-' }}</td>
                    <td class="small">{{ m.recordedBy }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        } @else {
          <div class="empty"><i class="fa-solid fa-inbox"></i> Aucun mouvement pour ce produit.</div>
        }
      </div>

      <div class="actions-bar">
        <a class="btn btn-ghost" routerLink="/"><i class="fa-solid fa-arrow-left"></i> Retour</a>
        @if (auth.canWrite()) { <button class="btn btn-danger" type="button" (click)="remove(v.product)"><i class="fa-solid fa-trash"></i> Supprimer le produit</button> }
      </div>
    } @else if (error()) {
      <div class="msg msg-error"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div>
    } @else {
      <div class="empty"><i class="fa-solid fa-spinner fa-spin"></i> Chargement…</div>
    }
  `,
})
export class ProductDetail {
  private readonly api = inject(Api);
  protected readonly auth = inject(Auth);
  private readonly router = inject(Router);
  private readonly notifier = inject(Notifier);

  readonly id = input.required<string>();
  protected readonly view = signal<ProductView | null>(null);
  protected readonly error = signal('');

  constructor() {
    queueMicrotask(() => void this.load());
  }

  private async load(): Promise<void> {
    try {
      this.view.set(await this.api.get<ProductView>(`/api/products/${this.id()}`));
    } catch (error) {
      this.error.set(errorMessage(error));
    }
  }

  protected async download(format: 'pdf' | 'excel'): Promise<void> {
    try {
      await this.api.download(`/api/products/${this.id()}/export/${format}`);
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }

  protected async remove(product: Product): Promise<void> {
    if (!confirm(`Supprimer « ${product.name} » avec tous ses mouvements et alertes ? Cette action est définitive.`)) return;
    try {
      await this.api.delete(`/api/products/${product.id}`);
      this.notifier.ok(`Produit « ${product.name} » supprimé avec succès.`);
      await this.router.navigate(['/']);
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }
}
