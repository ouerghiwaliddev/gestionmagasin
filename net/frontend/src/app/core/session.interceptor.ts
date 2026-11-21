import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { Auth } from './auth';

/** Une réponse 401 (hors connexion / état de session) signifie que la session a expiré. */
export const sessionInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(Auth);
  return next(request).pipe(
    catchError((error: unknown) => {
      const isAuthCall = request.url.startsWith('/api/auth/');
      if (error instanceof HttpErrorResponse && error.status === 401 && !isAuthCall && auth.user()) {
        auth.expire();
      }
      return throwError(() => error);
    }),
  );
};
