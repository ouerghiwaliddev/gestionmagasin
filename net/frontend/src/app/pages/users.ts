import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, errorMessage } from '../core/api';
import { Auth } from '../core/auth';
import { Role, User } from '../core/models';
import { Notifier } from '../core/notifier';
import { Hero } from '../shared/hero';

interface Draft {
  displayName: string;
  role: Role;
}

@Component({
  selector: 'app-users',
  imports: [FormsModule, DatePipe, Hero],
  template: `
    <app-hero eyebrow="Paramétrage" title="Utilisateurs" lead="Comptes, rôles et mots de passe. Toute modification est tracée dans le journal d'audit." />

    <div class="card">
      <h2>Nouveau compte</h2>
      <form (ngSubmit)="create()" style="margin-top: 14px">
        <div class="grid grid-4">
          <div class="field"><label for="nu">Nom d'utilisateur <b>*</b></label>
            <input id="nu" name="username" class="input" maxlength="80" required autocomplete="off" [(ngModel)]="form.username" /></div>
          <div class="field"><label for="nd">Nom affiché <b>*</b></label>
            <input id="nd" name="displayName" class="input" maxlength="120" required [(ngModel)]="form.displayName" /></div>
          <div class="field"><label for="nr">Rôle</label>
            <select id="nr" name="role" class="input" [(ngModel)]="form.role">
              <option value="manager">Gestionnaire</option><option value="viewer">Consultation</option><option value="admin">Administrateur</option>
            </select></div>
          <div class="field"><label for="np">Mot de passe <b>*</b></label>
            <input id="np" name="password" type="password" class="input" minlength="8" required autocomplete="new-password" [(ngModel)]="form.password" />
            <p class="hint">8 caractères minimum.</p></div>
        </div>
        <div class="actions-bar" style="justify-content: flex-end">
          <button class="btn btn-success" type="submit"><i class="fa-solid fa-user-plus"></i> Créer le compte</button>
        </div>
      </form>
    </div>

    <div class="card flat" style="margin-top: 24px">
      <div class="table-wrap">
        <table class="table">
          <thead>
            <tr><th>Utilisateur</th><th>Nom affiché</th><th>Rôle</th><th>Dernière connexion</th><th>État</th><th></th></tr>
          </thead>
          <tbody>
            @for (u of users(); track u.id) {
              <tr>
                <td><b>{{ u.username }}</b></td>
                <td><input class="input sm" [name]="'dn' + u.id" maxlength="120" aria-label="Nom affiché" [(ngModel)]="drafts[u.id].displayName" /></td>
                <td>
                  <select class="input sm" [name]="'rl' + u.id" aria-label="Rôle" [(ngModel)]="drafts[u.id].role">
                    <option value="manager">Gestionnaire</option><option value="viewer">Consultation</option><option value="admin">Administrateur</option>
                  </select>
                </td>
                <td class="small nowrap">{{ u.lastLoginAt ? (u.lastLoginAt | date: 'dd/MM/yyyy HH:mm') : 'Jamais' }}</td>
                <td><span class="badge" [class]="'badge ' + (u.active ? 'badge-ok' : 'badge-muted')">{{ u.active ? 'Actif' : 'Désactivé' }}</span></td>
                <td class="right nowrap">
                  <button class="btn btn-ghost sm" type="button" (click)="save(u)"><i class="fa-solid fa-floppy-disk"></i> Enregistrer</button>
                  <button class="btn btn-ghost sm" type="button" (click)="resetPassword(u)"><i class="fa-solid fa-key"></i></button>
                  @if (u.active) {
                    <button class="btn btn-danger sm" type="button" [disabled]="u.id === auth.user()?.id" (click)="setActive(u, false)">Désactiver</button>
                  } @else {
                    <button class="btn btn-success sm" type="button" (click)="setActive(u, true)">Activer</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </div>
  `,
})
export class Users {
  private readonly api = inject(Api);
  private readonly notifier = inject(Notifier);
  protected readonly auth = inject(Auth);

  protected readonly users = signal<User[]>([]);
  protected drafts: Record<number, Draft> = {};
  protected form = { username: '', displayName: '', role: 'manager' as Role, password: '' };

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const users = await this.api.get<User[]>('/api/admin/users');
      this.drafts = Object.fromEntries(users.map((u) => [u.id, { displayName: u.displayName, role: u.role }]));
      this.users.set(users);
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }

  protected async create(): Promise<void> {
    try {
      await this.api.post('/api/admin/users', this.form);
      this.form = { username: '', displayName: '', role: 'manager', password: '' };
      this.notifier.ok('Compte créé.');
      await this.load();
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }

  protected async save(user: User): Promise<void> {
    try {
      await this.api.put(`/api/admin/users/${user.id}`, this.drafts[user.id]);
      this.notifier.ok('Compte modifié.');
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
    await this.load();
  }

  protected async resetPassword(user: User): Promise<void> {
    const password = prompt(`Nouveau mot de passe pour « ${user.username} » (8 caractères minimum) :`);
    if (password === null) return;
    try {
      await this.api.post(`/api/admin/users/${user.id}/reset-password`, { password });
      this.notifier.ok('Mot de passe réinitialisé.');
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
  }

  protected async setActive(user: User, active: boolean): Promise<void> {
    try {
      await this.api.post(`/api/admin/users/${user.id}/${active ? 'activate' : 'deactivate'}`);
      this.notifier.ok(active ? 'Compte activé.' : 'Compte désactivé.');
    } catch (error) {
      this.notifier.error(errorMessage(error));
    }
    await this.load();
  }
}
