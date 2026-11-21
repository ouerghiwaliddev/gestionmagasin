import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Hero } from '../shared/hero';

@Component({
  selector: 'app-forbidden',
  imports: [RouterLink, Hero],
  template: `
    <app-hero eyebrow="Erreur 403" title="Accès refusé" lead="Votre rôle ne permet pas d'ouvrir cette page." />
    <div class="card center">
      <p class="sub">Cette section est réservée aux administrateurs.</p>
      <a class="btn btn-primary" routerLink="/"><i class="fa-solid fa-house"></i> Retour au tableau de bord</a>
    </div>
  `,
})
export class Forbidden {}

@Component({
  selector: 'app-not-found',
  imports: [RouterLink, Hero],
  template: `
    <app-hero eyebrow="Erreur 404" title="Page introuvable" lead="La page demandée n'existe pas ou a été déplacée." />
    <div class="card center">
      <a class="btn btn-primary" routerLink="/"><i class="fa-solid fa-house"></i> Retour au tableau de bord</a>
    </div>
  `,
})
export class NotFound {}
