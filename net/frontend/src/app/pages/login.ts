import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { errorMessage } from '../core/api';
import { Auth } from '../core/auth';

@Component({
  selector: 'app-login',
  imports: [FormsModule],
  template: `
    <div class="auth-wrap">
      <div class="card auth-card">
        <img class="logo" src="logo-bnaassurances.png" alt="BNA Assurances" />
        <h2>Connexion</h2>
        <p class="sub">Accédez à la gestion de magasin avec votre compte.</p>

        @if (error()) {
          <div class="msg msg-error" role="alert"><i class="fa-solid fa-circle-exclamation"></i> {{ error() }}</div>
        }

        <form class="stack" (ngSubmit)="submit()">
          <div class="field">
            <label for="username">Nom d'utilisateur <b>*</b></label>
            <input id="username" name="username" class="input" [(ngModel)]="username" autocomplete="username" required autofocus />
          </div>
          <div class="field">
            <label for="password">Mot de passe <b>*</b></label>
            <input id="password" name="password" type="password" class="input" [(ngModel)]="password" autocomplete="current-password" required />
          </div>
          <button class="btn btn-primary block" type="submit" [disabled]="busy()">
            <i class="fa-solid fa-right-to-bracket"></i> {{ busy() ? 'Connexion…' : 'Se connecter' }}
          </button>
        </form>
      </div>
    </div>
  `,
})
export class Login {
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected username = '';
  protected password = '';
  protected readonly error = signal('');
  protected readonly busy = signal(false);

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.auth.login(this.username.trim(), this.password);
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
      // N'accepte qu'un chemin interne (pas de redirection vers un autre site).
      await this.router.navigateByUrl(returnUrl?.startsWith('/') && !returnUrl.startsWith('//') ? returnUrl : '/');
    } catch (error) {
      this.error.set(errorMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
