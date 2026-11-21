import { Injectable, signal } from '@angular/core';

export type NoticeKind = 'ok' | 'error' | 'warn' | 'info';

export interface Notice {
  id: number;
  kind: NoticeKind;
  text: string;
}

/** Messages éphémères affichés en haut de page (équivalent des « flash » de l'ancienne version). */
@Injectable({ providedIn: 'root' })
export class Notifier {
  private seq = 0;
  readonly notices = signal<Notice[]>([]);

  show(kind: NoticeKind, text: string): void {
    const id = ++this.seq;
    this.notices.update((list) => [...list, { id, kind, text }]);
    if (kind !== 'error') setTimeout(() => this.dismiss(id), 6000);
  }

  ok(text: string): void { this.show('ok', text); }
  error(text: string): void { this.show('error', text); }
  warn(text: string): void { this.show('warn', text); }

  dismiss(id: number): void {
    this.notices.update((list) => list.filter((n) => n.id !== id));
  }

  /** Efface les erreurs d'une page précédente ; les confirmations disparaissent seules. */
  clearErrors(): void {
    this.notices.update((list) => list.filter((n) => n.kind !== 'error'));
  }
}
