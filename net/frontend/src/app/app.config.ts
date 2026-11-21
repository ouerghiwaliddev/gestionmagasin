import { registerLocaleData } from '@angular/common';
import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import localeFr from '@angular/common/locales/fr';
import {
  ApplicationConfig,
  LOCALE_ID,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { Auth } from './core/auth';
import { sessionInterceptor } from './core/session.interceptor';

registerLocaleData(localeFr);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    // Le jeton CSRF (cookie XSRF-TOKEN) est renvoyé par Angular dans l'en-tête X-XSRF-TOKEN.
    provideHttpClient(
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
      withInterceptors([sessionInterceptor]),
    ),
    { provide: LOCALE_ID, useValue: 'fr' },
    // Vérifie la session (et initialise le jeton CSRF) avant d'afficher quoi que ce soit.
    provideAppInitializer(() => inject(Auth).load()),
  ],
};
