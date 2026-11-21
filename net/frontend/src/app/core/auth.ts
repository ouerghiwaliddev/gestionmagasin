import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api } from './api';
import { User } from './models';

@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly api = inject(Api);
  private readonly router = inject(Router);

  /** undefined : session pas encore vérifiée ; null : non connecté. */
  readonly user = signal<User | null | undefined>(undefined);
  readonly isAdmin = computed(() => this.user()?.role === 'admin');

  async load(): Promise<void> {
    try {
      this.user.set((await this.api.get<{ user: User | null }>('/api/auth/me')).user);
    } catch {
      this.user.set(null);
    }
  }

  async login(username: string, password: string): Promise<void> {
    const { user } = await this.api.post<{ user: User }>('/api/auth/login', { username, password });
    this.user.set(user);
  }

  async logout(): Promise<void> {
    try {
      await this.api.post('/api/auth/logout');
    } finally {
      this.expire();
    }
  }

  /** Session expirée ou fermée : retour à l'écran de connexion. */
  expire(): void {
    this.user.set(null);
    void this.router.navigate(['/login']);
  }
}
