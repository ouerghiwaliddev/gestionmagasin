import { Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Api, errorMessage } from '../core/api';
import { Product } from '../core/models';
import { Notifier } from '../core/notifier';
import { Hero } from '../shared/hero';

/** Création (/products/new) et modification (/products/:id/edit) d'un produit. */
@Component({
  selector: 'app-product-form',
  imports: [FormsModule, RouterLink, Hero],
  template: `
    <app-hero [eyebrow]="isEdit() ? 'Produit' : 'Catalogue'" [title]="isEdit() ? 'Modifier le produit' : 'Nouveau produit'"
              [lead]="isEdit() ? 'Le SKU et le stock ne se modifient que par des mouvements.' : 'Renseignez le produit et son stock de départ.'" />

    <div class="card">
      @if (error()) {
        <div class="msg msg-error" role="alert"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div>
      }
      <form (ngSubmit)="submit()">
        <div class="grid">
          <div class="field">
            <label for="name">Nom du produit <b>*</b></label>
            <input id="name" name="name" class="input" maxlength="100" required [(ngModel)]="name" />
          </div>
          <div class="field">
            <label for="sku">SKU <b>*</b></label>
            <input id="sku" name="sku" class="input" maxlength="50" required [disabled]="isEdit()" [(ngModel)]="sku" />
            @if (isEdit()) { <p class="hint">Le SKU n'est pas modifiable.</p> }
          </div>
          <div class="field span2">
            <label for="description">Description</label>
            <textarea id="description" name="description" class="input" [(ngModel)]="description"></textarea>
          </div>
          @if (!isEdit()) {
            <div class="field">
              <label for="initialStock">Stock initial</label>
              <input id="initialStock" name="initialStock" type="number" min="0" step="1" class="input" [(ngModel)]="initialStock" />
            </div>
          }
          <div class="field">
            <label for="threshold">Seuil d'alerte</label>
            <input id="threshold" name="threshold" type="number" min="0" step="1" class="input" [(ngModel)]="threshold" />
            <p class="hint">Une alerte est créée quand le stock passe sous ce seuil.</p>
          </div>
        </div>
        <div class="actions-bar">
          <a class="btn btn-ghost" [routerLink]="isEdit() ? ['/products', id()] : ['/']"><i class="fa-solid fa-arrow-left"></i> Annuler</a>
          <button class="btn btn-success" type="submit" [disabled]="busy()">
            <i class="fa-solid fa-check"></i> {{ isEdit() ? 'Enregistrer' : 'Créer le produit' }}
          </button>
        </div>
      </form>
    </div>
  `,
})
export class ProductForm {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly notifier = inject(Notifier);

  /** Paramètre de route (liaison automatique) : présent uniquement en modification. */
  readonly id = input<string>();
  protected readonly isEdit = computed(() => !!this.id());

  protected readonly name = signal('');
  protected readonly sku = signal('');
  protected readonly description = signal('');
  protected readonly initialStock = signal(0);
  protected readonly threshold = signal(10);
  protected readonly error = signal('');
  protected readonly busy = signal(false);

  constructor() {
    queueMicrotask(() => void this.loadIfEditing());
  }

  private async loadIfEditing(): Promise<void> {
    const id = this.id();
    if (!id) return;
    try {
      const { product } = await this.api.get<{ product: Product }>(`/api/products/${id}`);
      this.name.set(product.name);
      this.sku.set(product.sku);
      this.description.set(product.description);
      this.threshold.set(product.threshold);
    } catch (error) {
      this.error.set(errorMessage(error));
    }
  }

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      if (this.isEdit()) {
        const product = await this.api.put<Product>(`/api/products/${this.id()}`, {
          name: this.name(), description: this.description(), threshold: Number(this.threshold()),
        });
        this.notifier.ok(`Produit « ${product.name} » modifié avec succès.`);
        await this.router.navigate(['/products', product.id]);
      } else {
        const product = await this.api.post<Product>('/api/products', {
          name: this.name(), sku: this.sku(), description: this.description(),
          initialStock: Number(this.initialStock()), threshold: Number(this.threshold()),
        });
        this.notifier.ok(`Produit « ${product.name} » ajouté avec succès.`);
        await this.router.navigate(['/']);
      }
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
