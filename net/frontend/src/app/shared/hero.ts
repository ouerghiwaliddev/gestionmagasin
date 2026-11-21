import { Component, input } from '@angular/core';

/** Bandeau d'en-tête de page (dégradé bleu, coins du logo) ; le contenu projeté sert d'actions. */
@Component({
  selector: 'app-hero',
  template: `
    <section class="hero">
      <div>
        @if (eyebrow()) { <p class="eyebrow">{{ eyebrow() }}</p> }
        <h1>{{ title() }}</h1>
        @if (lead()) { <p class="lead">{{ lead() }}</p> }
      </div>
      <div class="actions"><ng-content /></div>
    </section>
  `,
})
export class Hero {
  readonly title = input.required<string>();
  readonly eyebrow = input<string>('');
  readonly lead = input<string>('');
}
