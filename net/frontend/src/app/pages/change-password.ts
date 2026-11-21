import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Api, errorMessage } from '../core/api';
import { Notifier } from '../core/notifier';
import { Hero } from '../shared/hero';

@Component({
  selector: 'app-change-password',
  imports: [FormsModule, RouterLink, Hero],
  template: `
    <app-hero eyebrow="Mon compte" title="Changer le mot de passe" lead="Choisissez un mot de passe d'au moins 8 caractères." />

    <div class="card" style="max-width: 560px">
      @if (error()) {
        <div class="msg msg-error" role="alert"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div>
      }
      <form class="stack" (ngSubmit)="submit()">
        <div class="field">
          <label for="current">Mot de passe actuel <b>*</b></label>
          <input id="current" name="current" type="password" class="input" required autocomplete="current-password" [(ngModel)]="current" />
        </div>
        <div class="field">
          <label for="next">Nouveau mot de passe <b>*</b></label>
          <input id="next" name="next" type="password" class="input" minlength="8" required autocomplete="new-password" [(ngModel)]="next" />
        </div>
        <div class="field">
          <label for="confirmation">Confirmation <b>*</b></label>
          <input id="confirmation" name="confirmation" type="password" class="input" required autocomplete="new-password" [(ngModel)]="confirmation" />
        </div>
        <div class="actions-bar" style="margin-top: 4px">
          <a class="btn btn-ghost" routerLink="/"><i class="fa-solid fa-arrow-left"></i> Annuler</a>
          <button class="btn btn-success" type="submit" [disabled]="busy()"><i class="fa-solid fa-check"></i> Modifier</button>
        </div>
      </form>
    </div>
  `,
})
export class ChangePassword {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly notifier = inject(Notifier);

  protected current = '';
  protected next = '';
  protected confirmation = '';
  protected readonly error = signal('');
  protected readonly busy = signal(false);

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.post('/api/auth/change-password', {
        currentPassword: this.current, newPassword: this.next, passwordConfirmation: this.confirmation,
      });
      this.notifier.ok('Mot de passe modifié.');
      await this.router.navigate(['/']);
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
