import { DecimalPipe } from '@angular/common';
import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Api, errorMessage } from '../core/api';
import { Lookups, Movement, MovementType, Product } from '../core/models';
import { Notifier } from '../core/notifier';
import { Hero } from '../shared/hero';

const ENTRY_REASONS = [
  { value: 'Achat', label: 'Achat fournisseur' },
  { value: 'Retour client', label: 'Retour client' },
  { value: 'Transfert interne', label: 'Transfert interne' },
  { value: 'Inventaire', label: 'Correction inventaire' },
  { value: 'Autre', label: 'Autre entrée' },
];

const EXIT_REASONS = [
  { value: 'Vente', label: 'Vente client' },
  { value: 'Retour fournisseur', label: 'Retour fournisseur' },
  { value: 'Casse', label: 'Produit cassé / endommagé' },
  { value: 'Perte', label: 'Perte / disparition' },
  { value: 'Usage interne', label: 'Usage interne' },
  { value: 'Autre', label: 'Autre sortie' },
];

@Component({
  selector: 'app-movement-form',
  imports: [FormsModule, RouterLink, DecimalPipe, Hero],
  template: `
    <app-hero eyebrow="Stock" title="Mouvement de stock" lead="Enregistrez une entrée ou une sortie : prix HT, taxe, TTC et contrepartie." />

    @if (product(); as p) {
      <div class="card">
        <div class="kpis" style="margin-bottom: 22px">
          <div class="kpi main"><b style="font-size: 18px">{{ p.name }}</b><span>{{ p.sku }}</span></div>
          <div class="kpi" [class.danger]="p.isCritical" [class.warn]="p.isLow"><b>{{ p.currentStock }}</b><span>Stock actuel</span></div>
          <div class="kpi"><b>{{ p.threshold }}</b><span>Seuil d'alerte</span></div>
        </div>

        @if (error()) {
          <div class="msg msg-error" role="alert"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div>
        }

        <form (ngSubmit)="submit()">
          <div class="options" role="radiogroup" aria-label="Type de mouvement">
            <label class="option" [class.selected]="type() === 'IN'">
              <input type="radio" name="type" value="IN" [checked]="type() === 'IN'" (change)="type.set('IN')" />
              <span class="name"><i class="fa-solid fa-arrow-down"></i> Entrée</span>
              <span class="desc">Ajout au stock (fournisseur)</span>
            </label>
            <label class="option out" [class.selected]="type() === 'OUT'">
              <input type="radio" name="type" value="OUT" [checked]="type() === 'OUT'" (change)="type.set('OUT')" />
              <span class="name"><i class="fa-solid fa-arrow-up"></i> Sortie</span>
              <span class="desc">Retrait du stock (agent ou service)</span>
            </label>
          </div>

          <div class="grid grid-3" style="margin-top: 20px">
            <div class="field">
              <label for="quantity">Quantité <b>*</b></label>
              <input id="quantity" name="quantity" type="number" class="input" min="1" step="1" required [(ngModel)]="quantity" />
            </div>
            <div class="field">
              <label for="unitPrice">Prix unitaire HT @if (type() === 'IN') { <b>*</b> }</label>
              <div class="input-group">
                <input id="unitPrice" name="unitPrice" type="number" class="input" min="0" step="0.01" placeholder="0.00"
                       [required]="type() === 'IN'" [(ngModel)]="unitPrice" />
                <span class="addon">TND</span>
              </div>
              <p class="hint">{{ type() === 'IN' ? "Obligatoire à l'entrée" : 'Facultatif à la sortie' }}</p>
            </div>
            <div class="field">
              <label for="taxRate">Taxe</label>
              <div class="input-group">
                <select id="taxRate" name="taxRate" class="input" [(ngModel)]="taxRate">
                  @for (rate of lookups()?.taxRates ?? []; track rate) { <option [ngValue]="rate">{{ rate }}</option> }
                </select>
                <span class="addon">%</span>
              </div>
            </div>
          </div>

          <div class="total-box" style="margin-top: 18px" aria-live="polite">
            <div><span>Total HT</span><b>{{ totals().ht | number: '1.2-2' }} TND</b></div>
            <div><span>Taxe</span><b>{{ totals().tax | number: '1.2-2' }} TND</b></div>
            <div><span>Total TTC</span><b>{{ totals().ttc | number: '1.2-2' }} TND</b></div>
          </div>

          @if (type() === 'IN') {
            <div class="fieldset">
              <div class="legend"><i class="fa-solid fa-truck" style="color: var(--g)"></i> Fournisseur <b class="req">*</b></div>
              <select name="supplierId" class="input" required [(ngModel)]="supplierId" aria-label="Fournisseur">
                <option [ngValue]="null">Sélectionnez un fournisseur…</option>
                @for (s of lookups()?.suppliers ?? []; track s.id) { <option [ngValue]="s.id">{{ s.name }}</option> }
              </select>
              @if (!(lookups()?.suppliers?.length)) {
                <p class="hint" style="color: var(--danger)">
                  Aucun fournisseur : <a routerLink="/settings/referentials">ajoutez-en dans les listes</a>.
                </p>
              }
            </div>
          } @else {
            <div class="fieldset">
              <div class="legend"><i class="fa-solid fa-diagram-project" style="color: var(--g)"></i> Agent / Service <b class="req">*</b></div>
              <div class="options" role="radiogroup" aria-label="Destinataire">
                <label class="option" [class.selected]="recipientType() === 'agent'">
                  <input type="radio" name="recipient" value="agent" [checked]="recipientType() === 'agent'" (change)="recipientType.set('agent')" />
                  <span class="name">Agent</span><span class="desc">Sortie remise à un agent</span>
                </label>
                <label class="option" [class.selected]="recipientType() === 'service'">
                  <input type="radio" name="recipient" value="service" [checked]="recipientType() === 'service'" (change)="recipientType.set('service')" />
                  <span class="name">Service</span><span class="desc">Sortie destinée à un service</span>
                </label>
              </div>

              @if (recipientType() === 'agent') {
                <div class="field" style="margin-top: 16px">
                  <label for="agentId">Agent <b>*</b></label>
                  <select id="agentId" name="agentId" class="input" required [(ngModel)]="agentId">
                    <option [ngValue]="null">Sélectionnez un agent…</option>
                    @for (a of lookups()?.agents ?? []; track a.id) { <option [ngValue]="a.id">{{ a.name }}</option> }
                  </select>
                  @if (!(lookups()?.agents?.length)) {
                    <p class="hint" style="color: var(--danger)">Aucun agent : <a routerLink="/settings/referentials">ajoutez-en dans les listes</a>.</p>
                  }
                </div>
              }
              @if (recipientType() === 'service') {
                <div class="field" style="margin-top: 16px">
                  <label for="serviceId">Service <b>*</b></label>
                  <select id="serviceId" name="serviceId" class="input" required [(ngModel)]="serviceId">
                    <option [ngValue]="null">Sélectionnez un service…</option>
                    @for (s of lookups()?.services ?? []; track s.id) { <option [ngValue]="s.id">{{ s.name }}</option> }
                  </select>
                  @if (!(lookups()?.services?.length)) {
                    <p class="hint" style="color: var(--danger)">Aucun service : <a routerLink="/settings/referentials">ajoutez-en dans les listes</a>.</p>
                  }
                </div>
              }
            </div>
          }

          <div class="field" style="margin-top: 20px">
            <label for="reason">Motif</label>
            <select id="reason" name="reason" class="input" [(ngModel)]="reason">
              <option value="">Sélectionnez un motif…</option>
              @for (r of reasons(); track r.value) { <option [value]="r.value">{{ r.label }}</option> }
            </select>
          </div>

          @if (p.isCritical) {
            <div class="msg msg-error" style="margin-top: 18px"><i class="fa-solid fa-circle-exclamation"></i> Alerte critique : le stock de ce produit est épuisé.</div>
          } @else if (p.isLow) {
            <div class="msg msg-warn" style="margin-top: 18px"><i class="fa-solid fa-triangle-exclamation"></i> Stock faible : inférieur au seuil d'alerte.</div>
          }

          <div class="actions-bar">
            <a class="btn btn-ghost" [routerLink]="['/products', p.id]"><i class="fa-solid fa-arrow-left"></i> Retour au produit</a>
            <button class="btn btn-success" type="submit" [disabled]="busy()"><i class="fa-solid fa-check"></i> Enregistrer le mouvement</button>
          </div>
        </form>
      </div>
    } @else if (loadError()) {
      <div class="msg msg-error"><i class="fa-solid fa-circle-exclamation"></i> {{ loadError() }}</div>
    } @else {
      <div class="empty"><i class="fa-solid fa-spinner fa-spin"></i> Chargement…</div>
    }
  `,
})
export class MovementForm {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly notifier = inject(Notifier);

  readonly id = input.required<string>();

  protected readonly product = signal<Product | null>(null);
  protected readonly lookups = signal<Lookups | null>(null);
  protected readonly loadError = signal('');
  protected readonly error = signal('');
  protected readonly busy = signal(false);

  protected readonly type = signal<MovementType>('IN');
  protected readonly recipientType = signal<'' | 'agent' | 'service'>('');
  protected readonly quantity = signal<number | null>(null);
  protected readonly unitPrice = signal<number | null>(null);
  protected readonly taxRate = signal(19);
  protected readonly supplierId = signal<number | null>(null);
  protected readonly agentId = signal<number | null>(null);
  protected readonly serviceId = signal<number | null>(null);
  protected readonly reason = signal('');

  protected readonly reasons = computed(() => (this.type() === 'IN' ? ENTRY_REASONS : EXIT_REASONS));

  /** Totaux affichés en direct : taxe arrondie au centime comme côté serveur. */
  protected readonly totals = computed(() => {
    const ht = (Number(this.quantity()) || 0) * (Number(this.unitPrice()) || 0);
    const tax = Math.round(ht * (Number(this.taxRate()) || 0)) / 100;
    return { ht, tax, ttc: ht + tax };
  });

  constructor() {
    queueMicrotask(() => void this.load());
  }

  private async load(): Promise<void> {
    try {
      const [{ product }, lookups] = await Promise.all([
        this.api.get<{ product: Product }>(`/api/products/${this.id()}`),
        this.api.get<Lookups>('/api/lookups'),
      ]);
      this.product.set(product);
      this.lookups.set(lookups);
      this.taxRate.set(lookups.defaultTaxRate);
    } catch (error) {
      this.loadError.set(errorMessage(error));
    }
  }

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    const isEntry = this.type() === 'IN';
    const price = this.unitPrice();
    try {
      const result = await this.api.post<{ movement: Movement; warning: string | null }>(
        `/api/products/${this.id()}/movements`,
        {
          quantity: Number(this.quantity()),
          movementType: this.type(),
          unitPrice: price === null || (price as unknown) === '' ? null : Number(price),
          taxRate: Number(this.taxRate()),
          reason: this.reason(),
          supplierId: isEntry ? this.supplierId() : null,
          recipientType: isEntry ? null : this.recipientType() || null,
          agentId: !isEntry && this.recipientType() === 'agent' ? this.agentId() : null,
          serviceId: !isEntry && this.recipientType() === 'service' ? this.serviceId() : null,
        },
      );
      const quantity = Math.abs(result.movement.quantityChange);
      this.notifier.ok(`Mouvement enregistré : ${isEntry ? 'entrée' : 'sortie'} de ${quantity} unités.`);
      if (result.warning) this.notifier.warn(result.warning);
      await this.router.navigate(['/products', this.id()]);
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
