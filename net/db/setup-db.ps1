<#
  Crée la base PostgreSQL et toutes les tables (idempotent : ne touche pas à une base déjà initialisée).
  Usage : .\setup-db.ps1 -Password "xxx" [-DbHost localhost] [-Port 5432] [-User postgres] [-Database gestionmagasin]
  Le mot de passe peut aussi venir de la variable d'environnement PGPASSWORD.
  Prérequis : psql (client PostgreSQL) dans le PATH, ou installé sous C:\Program Files\PostgreSQL\*\bin.
#>
param(
    [string]$DbHost = "localhost",
    [int]$Port = 5432,
    [string]$User = "postgres",
    [string]$Database = "gestionmagasin",
    [string]$Password = $env:PGPASSWORD
)
$ErrorActionPreference = "Stop"
$schema = Join-Path $PSScriptRoot "schema.sql"
if (-not (Test-Path $schema)) { throw "schema.sql introuvable ($schema)" }
if (-not $Password) { throw "Mot de passe requis : -Password ou variable PGPASSWORD." }
if ($Database -notmatch '^[A-Za-z0-9_]+$') { throw "Nom de base invalide (lettres, chiffres, _ uniquement)." }

$psql = (Get-Command psql -ErrorAction SilentlyContinue).Source
if (-not $psql) {
    $psql = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\psql.exe" -ErrorAction SilentlyContinue |
        Sort-Object { [int](Split-Path (Split-Path (Split-Path $_.FullName))  -Leaf) } -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $psql) { throw "psql introuvable : installer le client PostgreSQL ou l'ajouter au PATH." }

$env:PGPASSWORD = $Password
$conn = @("-h", $DbHost, "-p", $Port, "-U", $User, "-v", "ON_ERROR_STOP=1", "-X", "-q", "-t", "-A")

function Invoke-Psql([string]$db, [string[]]$more) {
    $out = & $psql @conn -d $db @more
    if ($LASTEXITCODE -ne 0) { throw "psql a échoué (code $LASTEXITCODE)." }
    return $out
}

# 1. Base
$exists = Invoke-Psql "postgres" @("-c", "SELECT 1 FROM pg_database WHERE datname = '$Database'")
if (-not $exists) {
    Invoke-Psql "postgres" @("-c", "CREATE DATABASE $Database ENCODING 'UTF8'") | Out-Null
    Write-Host "Base '$Database' créée."
} else { Write-Host "Base '$Database' déjà présente." }

# 2. Tables (seulement si la base est vide)
$tables = Invoke-Psql $Database @("-c", "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'Users'")
if ([int]$tables -eq 0) {
    Invoke-Psql $Database @("-1", "-f", $schema) | Out-Null   # -1 : tout dans une transaction
    Write-Host "Tables créées."
} else { Write-Host "Tables déjà présentes : rien à faire." }

Write-Host "`nChaîne de connexion de l'application :"
Write-Host "  Host=$DbHost;Port=$Port;Database=$Database;Username=$User;Password=***"
Write-Host "Le premier administrateur est créé au 1er démarrage (Admin__Username / Admin__Password)."
