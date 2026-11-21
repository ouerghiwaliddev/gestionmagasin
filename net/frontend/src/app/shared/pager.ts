import { Component, computed, input, output } from '@angular/core';

@Component({
  selector: 'app-pager',
  template: `
    @if (pages() > 1) {
      <nav class="pager" aria-label="Pagination">
        <button type="button" [disabled]="page() <= 1" (click)="pageChange.emit(page() - 1)" aria-label="Page précédente">
          <i class="fa-solid fa-chevron-left"></i>
        </button>
        @for (n of numbers(); track $index) {
          @if (n === 0) {
            <button type="button" disabled>…</button>
          } @else {
            <button type="button" [class.active]="n === page()" [attr.aria-current]="n === page() ? 'page' : null" (click)="pageChange.emit(n)">{{ n }}</button>
          }
        }
        <button type="button" [disabled]="page() >= pages()" (click)="pageChange.emit(page() + 1)" aria-label="Page suivante">
          <i class="fa-solid fa-chevron-right"></i>
        </button>
      </nav>
    }
  `,
})
export class Pager {
  readonly page = input.required<number>();
  readonly pages = input.required<number>();
  readonly pageChange = output<number>();

  /** Numéros affichés ; 0 représente une ellipse. */
  protected readonly numbers = computed(() => {
    const total = this.pages();
    const current = this.page();
    const shown = new Set([1, total, current - 1, current, current + 1]);
    const list: number[] = [];
    let previous = 0;
    for (const n of [...shown].filter((n) => n >= 1 && n <= total).sort((a, b) => a - b)) {
      if (previous && n - previous > 1) list.push(0);
      list.push(n);
      previous = n;
    }
    return list;
  });
}
