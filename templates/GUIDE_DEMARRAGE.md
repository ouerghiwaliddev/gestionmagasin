# 🚀 Guide de Démarrage Rapide - Gestion de Stock

## Installation et Lancement (2 minutes)

### Étape 1: Prérequis
- Python 3.10+ installé
- Terminal/PowerShell disponible

### Étape 2: Installation des Dépendances
```bash
cd c:\mes vs code\gestionmagasin
python -m pip install -r requirements.txt
```

### Étape 3: Lancer l'Application
```bash
python app.py
```

Vous verrez:
```
* Running on http://127.0.0.1:5000
```

### Étape 4: Accéder à l'Application
Ouvrez votre navigateur et allez à:
```
http://127.0.0.1:5000/
```

## 📖 Tutoriel Rapide (5 minutes)

### 1️⃣ Ajouter un Produit
1. Cliquez sur **"Ajouter Produit"** (navbar)
2. Remplissez le formulaire:
   - **Nom**: "Macbook Pro 14 pouces"
   - **SKU**: "MB-PRO-14-2024"
   - **Description**: "Ordinateur portable haute performance"
   - **Stock Initial**: 5
   - **Seuil d'alerte**: 2
3. Cliquez **"Ajouter le Produit"**

### 2️⃣ Consulter le Dashboard
- Voyez les KPIs en haut
- Trouvez votre produit dans le tableau
- Stock OK: 5 unités

### 3️⃣ Enregistrer une Vente
1. Sur le produit, cliquez **"Mouvement"** (icône de flèches)
2. Sélectionnez **"Sortie"**
3. **Quantité**: 2
4. **Motif**: "Vente client"
5. Cliquez **"Enregistrer Mouvement"**
6. Le stock devient: 3 unités ✅

### 4️⃣ Générer une Alerte
1. Enregistrez 2 sorties de plus (Sortie de 2 + Sortie de 1)
2. Stock final: 0 unités
3. 🚨 Une alerte "RUPTURE" apparaît!

### 5️⃣ Consulter l'Historique
1. Cliquez **"Mouvements"** (navbar)
2. Voyez toutes vos transactions
3. Filtrez par produit

### 6️⃣ Gérer les Alertes
1. Cliquez **"Alertes"** (navbar badge rouge)
2. Voyez l'alerte de rupture
3. Cliquez **"Résoudre"** ou **"Mouvement"** (pour réapprovisionner)

## 🎯 Cas d'Usage Courants

### Recevoir un Achat Fournisseur
```
Produit: Laptop Dell
Type: Entrée
Quantité: 20
Motif: Achat fournisseur
→ Stock augmente
```

### Enregistrer une Casse/Perte
```
Produit: Smartphone X
Type: Sortie
Quantité: 1
Motif: Produit cassé/endommagé
→ Stock réduit
→ Alerte si stock faible
```

### Corriger Inventaire
```
Produit: Tablette
Type: Entrée
Quantité: 3
Motif: Correction inventaire
→ Augmente le stock
```

## 📊 Structure du Dashboard

```
┌─────────────────────────────────────────────┐
│  📊 Tableau de Bord                         │
├─────────────────────────────────────────────┤
│ [20 Produits] [450 Unités] [3 Ruptures]   │
│ [2 Faibles]   [15 Mvt/30j]  [1 Alerte]    │
├─────────────────────────────────────────────┤
│ ⚠️  ALERTES ACTIVES                        │
│ 🚨 RUPTURE: Produit X                      │
│ ⚠️  FAIBLE: Produit Y                      │
├─────────────────────────────────────────────┤
│ 📦 PRODUITS EN STOCK                       │
│ [Produit A] [10 ✅]  [Action ▼]           │
│ [Produit B] [2 ⚠️]   [Action ▼]           │
│ [Produit C] [0 🚨]   [Action ▼]           │
└─────────────────────────────────────────────┘
```

## 🔘 Navigation Rapide

| Bouton | Action |
|--------|--------|
| 👁️ | Voir détails produit |
| ↔️ | Enregistrer mouvement |
| ✏️ | Modifier produit |
| 🗑️ | Supprimer produit |

## 💡 Conseils

### Organiser vos Produits
- Utilisez des SKU cohérents (ex: PREFIX-CATEGORY-ID)
- Gardez les descriptions simples mais explicites
- Définissez les seuils selon votre activité

### Suivi Efficace
- Consultez "Mouvements" chaque semaine
- Traitez rapidement les alertes
- Utilisez les motifs prédéfinis

### Sécurité
- Changez la `SECRET_KEY` en production
- Sauvegardez régulièrement `inventory.db`
- Utilisez HTTPS en production

## 🆘 Problèmes Courants

**Q: L'app démarre mais me donne une erreur?**
A: Supprimez `inventory.db` et redémarrez

**Q: Je vois "Port 5000 en utilisation"?**
A: Changez le port dans `app.py` ligne 330

**Q: Comment arrêter l'application?**
A: Appuyez sur `Ctrl+C` dans le terminal

## 📚 Pour Plus d'Infos

Consultez `README.md` pour:
- Architecture complète
- Toutes les routes API
- Structure du projet
- Dépannage approfondi

## 🎓 Fonctionnalités Avancées

### Filtrer l'Historique
```
Aller à: Mouvements
Filtrer par: [Sélectionner produit]
Pagination: Pages disponibles
```

### Voir le Détail d'un Produit
```
Cliquer: Produit dans dashboard
Voir: Stock, seuil, détails
Historique: Tous les mouvements
```

### Gérer Plusieurs Produits
```
Produit 1: 50 unités [OK]
Produit 2: 3 unités [⚠️ Faible]
Produit 3: 0 unités [🚨 Rupture]

Actions: Mouvements individuels
```

## 🚀 Prêt à Commencer!

```bash
# 1. Démarrer l'app
python app.py

# 2. Ouvrir le navigateur
http://127.0.0.1:5000/

# 3. Ajouter un produit
Navbar → "Ajouter Produit"

# 4. Enregistrer mouvements
Produit → "Mouvement"

# 5. Consulter alertes
Navbar → "Alertes"
```

---

**Besoin d'aide? Consultez README.md pour la documentation complète**

Bon gestion de stock! 📦✨
