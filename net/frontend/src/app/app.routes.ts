import { Routes } from '@angular/router';
import { adminGuard, authGuard, guestGuard, writeGuard } from './core/guards';

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    title: 'Connexion',
    loadComponent: () => import('./pages/login').then((m) => m.Login),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', title: 'Tableau de bord', loadComponent: () => import('./pages/dashboard').then((m) => m.Dashboard) },
      { path: 'products/new', canActivate: [writeGuard], title: 'Nouveau produit', loadComponent: () => import('./pages/product-form').then((m) => m.ProductForm) },
      { path: 'products/:id', title: 'Produit', loadComponent: () => import('./pages/product-detail').then((m) => m.ProductDetail) },
      { path: 'products/:id/edit', canActivate: [writeGuard], title: 'Modifier le produit', loadComponent: () => import('./pages/product-form').then((m) => m.ProductForm) },
      { path: 'products/:id/movement', canActivate: [writeGuard], title: 'Mouvement de stock', loadComponent: () => import('./pages/movement-form').then((m) => m.MovementForm) },
      { path: 'movements', title: 'Mouvements', loadComponent: () => import('./pages/movements').then((m) => m.Movements) },
      { path: 'alerts', title: 'Alertes', loadComponent: () => import('./pages/alerts').then((m) => m.Alerts) },
      { path: 'inventory', title: 'Inventaire', loadComponent: () => import('./pages/inventory').then((m) => m.InventoryPage) },
      { path: 'settings/referentials', title: 'Fournisseurs, agents, services', loadComponent: () => import('./pages/referentials').then((m) => m.Referentials) },
      { path: 'settings/users', canActivate: [adminGuard], title: 'Utilisateurs', loadComponent: () => import('./pages/users').then((m) => m.Users) },
      { path: 'settings/audit', canActivate: [adminGuard], title: "Journal d'audit", loadComponent: () => import('./pages/audit').then((m) => m.AuditPage) },
      { path: 'account/password', title: 'Changer le mot de passe', loadComponent: () => import('./pages/change-password').then((m) => m.ChangePassword) },
      { path: 'forbidden', title: 'Accès refusé', loadComponent: () => import('./pages/errors').then((m) => m.Forbidden) },
      { path: '**', title: 'Page introuvable', loadComponent: () => import('./pages/errors').then((m) => m.NotFound) },
    ],
  },
];
