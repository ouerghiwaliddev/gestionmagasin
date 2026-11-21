import { DecimalPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, errorMessage } from '../core/api';
import { Inventory } from '../core/models';
import { Notifier } from '../core/notifier';
import { Hero } from '../shared/hero';

@Component({
  selector: 'app-inventory',
  imports: [FormsModule, RouterLink, DecimalPipe, Hero],
  template: `
    <app-hero eyebrow="Exercice" [title]="'Inventaire ' + (data()?.year ?? '')"
              lead="Stock à la clôture de l'exercice et coût moyen pondéré TTC, cumulé depuis l'origine.">
      <button class="btn btn-light" type="button" (click)="download('pdf')"><i class="fa-solid fa-file-pdf"></i> PDF</button>
      <button class="btn btn-light" type="button" (click)="download('excel')"><i class="fa-solid fa-file-excel"></i> Excel</button>
    </app-hero>

    @if (data(); as d) {
      <div class="card">
        <div class="field" style="max-width: 240px">
          <label for="exercice">Exercice</label>
          <select id="exercice" class="input" [ngModel]="d.year" (ngModelChange)="load($event)">
            @for (y of d.years; track y) { <option [ngValue]="y">{{ y }}</option> }
          </select>
        </div>
        <p class="muted small" style="margin: 14px 0 0">
          Stock à la clôture du 31/12/{{ d.year }}. <b>CMP TTC cumulé</b> = total TTC de toutes les entrées depuis l'origine
          jusqu'au 31/12/{{ d.year }} ÷ quantités entrées sur la même période. Valeur du stock = stock à la clôture × CMP.
        </p>
      </div>

      @if (d.missingCmp) {
        <div class="msg msg-warn" style="margin-top: 20px">
          <i class="fa-solid fa-triangle-exclamation"></i>
          {{ d.missingCmp }} produit(s) en stock n'ont aucune entrée jusqu'à {{ d.year }} : leur CMP et leur valeur ne sont pas calculables.
        </div>
      }

      <div class="card flat" style="margin-top: 20px">
        @if (d.items.length) {
          <div class="table-wrap">
            <table class="table">
              <thead>
                <tr>
                  <th>Produit</th><th class="num">Stock à la clôture</th><th class="num">Entrées {{ d.year }}</th>
                  <th class="num">Entrées cumulées</th><th class="num">Total TTC cumulé</th>
                  <th class="num">CMP TTC cumulé</th><th class="num">Valeur du stock</th>
                </tr>
              </thead>
              <tbody>
                @for (i of d.items; track i.productId) {
                  <tr>
                    <td><a [routerLink]="['/products', i.productId]">{{ i.name }}</a><br /><span class="muted small">{{ i.sku }}</span></td>
                    <td class="num">{{ i.stock }}</td>
                    <td class="num">{{ i.entryQuantity }}</td>
                    <td class="num">{{ i.cumulQuantity }}</td>
                    <td class="num">{{ i.cumulTotalTtc | number: '1.2-2' }} TND</td>
                    <td class="num">@if (i.cmp !== null) { {{ i.cmp | number: '1.3-3' }} TND } @else { - }</td>
                    <td class="num"><b>@if (i.stockValue !== null) { {{ i.stockValue | number: '1.2-2' }} TND } @else { - }</b></td>
                  </tr>
                }
              </tbody>
              <tfoot>
                <tr><td colspan="6" class="num">Valeur totale du stock</td><td class="num">{{ d.totalValue | number: '1.2-2' }} TND</td></tr>
              </tfoot>
            </table>
          </div>
        } @else {
          <div class="empty"><i class="fa-solid fa-box-open"></i> Aucun produit pour cet exercice.</div>
        }
      </div>
    } @else {
      <div class="empty"><i class="fa-solid fa-spinner fa-spin"></i> Chargement…</div>
    }
  `,
})
export class InventoryPage {
  private readonly api = inject(Api);
  private readonly notifier = inject(Notifier);
  protected readonly data = signal<Inventory | null>(null);

  constructor() {
    void this.load();
  }

  protected async load(year?: number): Promise<void> {
    try {
      this.data.set(await this.api.get<Inventory>('/api/inventory', { exercice: year }));
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }

  protected async download(format: 'pdf' | 'excel'): Promise<void> {
    try {
      await this.api.download(`/api/export/inventory/${format}`, { exercice: this.data()?.year });
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }
}
