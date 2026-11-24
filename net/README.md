# Gestion de magasin : .NET 10 + Angular

Réécriture de l'application Flask (dossier parent) : API **ASP.NET Core (net10.0)** + **EF Core / PostgreSQL**,
interface **Angular** à la charte BNA Assurances (reprise de `EspaceClient/css/devis-auto.css`).

```
net/
├─ src/GestionMagasin.Api   API REST + hébergement de l'application Angular (wwwroot)
├─ tests/GestionMagasin.Tests   tests xUnit (API de bout en bout, CMP, exports, import)
└─ frontend                 Angular (composants autonomes, signaux, chargement différé)
```

## Démarrage

Prérequis : SDK .NET capable de cibler net10.0 (runtime 10 installé), Node 20+.

```powershell
# 1. Premier administrateur (créé seulement si la base n'a aucun utilisateur)
$env:Admin__Username = "admin"
$env:Admin__Password = "mot-de-passe"

# 2. Construire l'interface dans src/GestionMagasin.Api/wwwroot
cd net/frontend; npm install; npm run build

# 3. Lancer (http://localhost:5237)
cd ../src/GestionMagasin.Api; dotnet run
```

Base **PostgreSQL** (≥ 13, extension `citext` disponible). Créer la base puis renseigner la chaîne de connexion
(`ConnectionStrings:Default` dans `appsettings.json`, ou la variable d'environnement) :

```powershell
psql -U postgres -c "CREATE DATABASE gestionmagasin"
$env:ConnectionStrings__Default = "Host=localhost;Port=5432;Database=gestionmagasin;Username=postgres;Password=postgres"
```

Les tables sont créées au démarrage. Ne jamais committer un vrai mot de passe : utiliser la variable d'environnement
ou `dotnet user-secrets`.

Le build .NET construit aussi l'interface : `dotnet build` lance `npm run build` si `wwwroot` est vide (ou avec
`-p:BuildFrontend=true`), `dotnet publish` la reconstruit toujours et l'inclut dans `wwwroot` du dossier publié
(`-p:BuildFrontend=false` pour l'ignorer). Une seule application sert donc l'API et le front.

Développement avec rechargement : `dotnet run` dans l'API, puis `npm start` dans `frontend`
(proxy `/api` vers `localhost:5237` défini dans `proxy.conf.json`, ouvrir http://localhost:4200).

## Reprise des données de l'ancienne version

```powershell
cd net/src/GestionMagasin.Api
dotnet run -- import-legacy "C:\chemin\vers\inventory.db"
```

Importe produits, mouvements, alertes, fournisseurs, agents et services (base cible vide uniquement).
Les anciennes saisies texte « direction/agent source → destination » sont conservées (colonne « Ancienne saisie »).
**Les comptes et le journal d'audit ne sont pas repris** : les mots de passe Flask (hachage Werkzeug) ne sont pas
vérifiables ici. Les comptes se recréent depuis *Paramétrage → Utilisateurs*.

## Sécurité

- Session par cookie `HttpOnly` + `SameSite=Strict`, vérification du compte (actif, rôle) à chaque requête.
- Protection CSRF : cookie `XSRF-TOKEN` renvoyé par Angular dans `X-XSRF-TOKEN` sur toute requête modifiante.
- API : tout est authentifié par défaut ; `/api/admin/*` réservé au rôle `admin`.
- Mettre l'application derrière HTTPS en production (le cookie suit le schéma de la requête).

## Tests

Les tests ont besoin d'un serveur PostgreSQL (une base temporaire est créée puis supprimée par test).
Connexion par défaut : `Host=localhost;Username=postgres;Password=postgres`, modifiable via `TEST_PG_CONNECTION`.

```powershell
dotnet test net/tests/GestionMagasin.Tests
```
