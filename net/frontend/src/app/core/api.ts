import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type Params = Record<string, string | number | null | undefined>;

/** Message d'erreur lisible extrait d'une réponse de l'API ({ message }). */
export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const message = error.error?.message;
    if (typeof message === 'string' && message) return message;
    if (error.status === 0) return 'Le serveur est injoignable.';
    if (error.status === 403) return "Vous n'avez pas les droits nécessaires.";
    if (error.status === 404) return 'Ressource introuvable.';
  }
  return 'Une erreur est survenue. Veuillez réessayer.';
}

function toParams(params?: Params): HttpParams {
  let result = new HttpParams();
  for (const [key, value] of Object.entries(params ?? {})) {
    if (value !== null && value !== undefined && value !== '') result = result.set(key, String(value));
  }
  return result;
}

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  get<T>(url: string, params?: Params): Promise<T> {
    return firstValueFrom(this.http.get<T>(url, { params: toParams(params) }));
  }

  post<T>(url: string, body: unknown = {}): Promise<T> {
    return firstValueFrom(this.http.post<T>(url, body));
  }

  put<T>(url: string, body: unknown = {}): Promise<T> {
    return firstValueFrom(this.http.put<T>(url, body));
  }

  delete<T>(url: string): Promise<T> {
    return firstValueFrom(this.http.delete<T>(url));
  }

  /** Télécharge un fichier (export Excel / PDF) en conservant le nom fourni par le serveur. */
  async download(url: string, params?: Params): Promise<void> {
    const response = await firstValueFrom(
      this.http.get(url, { params: toParams(params), responseType: 'blob', observe: 'response' }),
    );
    const disposition = response.headers.get('Content-Disposition') ?? '';
    const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
    const filename = match ? decodeURIComponent(match[1]) : 'export';
    const link = document.createElement('a');
    const objectUrl = URL.createObjectURL(response.body as Blob);
    link.href = objectUrl;
    link.download = filename;
    link.click();
    setTimeout(() => URL.revokeObjectURL(objectUrl), 10_000);
  }
}
