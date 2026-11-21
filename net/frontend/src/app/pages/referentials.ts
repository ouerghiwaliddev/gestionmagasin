import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, errorMessage } from '../core/api';
import { Reference, ReferenceSection } from '../core/models';
import { Notifier } from '../core/notifier';
import { Hero } from '../shared/hero';

@Component({
  selector: 'app-referentials',
  imports: [FormsModule, Hero],
  template: `
    <app-hero eyebrow="Paramétrage" title="Fournisseurs, agents, services"
              lead="Ces listes alimentent les mouvements : fournisseur à l'entrée, agent ou service à la sortie. Une entrée désactivée n'est plus proposée à la saisie mais reste visible dans l'historique." />

    <div class="grid grid-3" style="align-items: start">
      @for (section of sections(); track section.kind) {
        <div class="card flat">
          <div style="padding: 22px 22px 6px">
            <h2 style="margin: 0"><i class="fa-solid" [class]="section.icon"></i> {{ section.plural }} ({{ section.items.length }})</h2>
          </div>
          <form class="row" style="flex-wrap: nowrap; padding: 14px 22px" (ngSubmit)="create(section)">
            <input class="input sm" [name]="'new-' + section.kind" maxlength="120" required [placeholder]="'Nouveau : ' + section.singular.toLowerCase()"
                   [attr.aria-label]="'Nom du ' + section.singular.toLowerCase()" [(ngModel)]="newNames[section.kind]" />
            <button class="btn btn-primary sm" type="submit" title="Ajouter"><i class="fa-solid fa-plus"></i></button>
          </form>
          <div style="max-height: 460px; overflow-y: auto">
            @for (item of section.items; track item.id) {
              <div class="row" style="flex-wrap: nowrap; padding: 8px 22px; border-top: 1px solid var(--border)">
                <input class="input sm" [name]="section.kind + '-' + item.id" maxlength="120" aria-label="Nom"
                       [style.text-decoration]="item.active ? 'none' : 'line-through'" [style.color]="item.active ? '' : 'var(--muted)'"
                       [(ngModel)]="item.name" (keydown.enter)="rename(section, item); $event.preventDefault()" />
                <button class="btn btn-ghost sm" type="button" title="Renommer" (click)="rename(section, item)"><i class="fa-solid fa-check"></i></button>
                <button class="btn sm" [class]="item.active ? 'btn sm btn-danger' : 'btn sm btn-success'" type="button"
                        [title]="item.active ? 'Désactiver' : 'Réactiver'" (click)="toggle(section, item)">
                  <i class="fa-solid" [class]="item.active ? 'fa-eye-slash' : 'fa-eye'"></i>
                </button>
              </div>
            } @empty {
              <div class="empty" style="padding: 28px 20px">Aucun élément.</div>
            }
          </div>
        </div>
      }
    </div>
  `,
})
export class Referentials {
  private readonly api = inject(Api);
  private readonly notifier = inject(Notifier);

  protected readonly sections = signal<ReferenceSection[]>([]);
  protected newNames: Record<string, string> = {};

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      this.sections.set(await this.api.get<ReferenceSection[]>('/api/referentials'));
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }

  protected async create(section: ReferenceSection): Promise<void> {
    try {
      const created = await this.api.post<Reference>(`/api/referentials/${section.kind}`, { name: this.newNames[section.kind] ?? '' });
      this.newNames[section.kind] = '';
      this.notifier.ok(`${section.singular} « ${created.name} » ajouté.`);
      await this.load();
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }

  protected async rename(section: ReferenceSection, item: Reference): Promise<void> {
    try {
      await this.api.put(`/api/referentials/${section.kind}/${item.id}`, { name: item.name });
      this.notifier.ok(`${section.singular} renommé.`);
      await this.load();
    } catch (error) {
      this.notifier.error(errorMessage(error));
      await this.load(); // rétablit le nom enregistré
    }
  }

  protected async toggle(section: ReferenceSection, item: Reference): Promise<void> {
    try {
      const updated = await this.api.post<Reference>(`/api/referentials/${section.kind}/${item.id}/toggle`);
      this.notifier.ok(`${section.singular} ${updated.active ? 'réactivé' : 'désactivé'}.`);
      await this.load();
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }
}
