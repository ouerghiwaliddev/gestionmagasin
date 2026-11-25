import { HttpInterceptorFn } from '@angular/common/http';

/** Préfixe les appels /api avec le chemin de <base href> (ex. /GestionMagasin/ quand l'app est hébergée dans un sous-dossier IIS). */
export const apiBaseInterceptor: HttpInterceptorFn = (request, next) => {
  const base = new URL(document.baseURI).pathname.replace(/\/$/, '');
  if (base && request.url.startsWith('/api/')) {
    return next(request.clone({ url: base + request.url }));
  }
  return next(request);
};
