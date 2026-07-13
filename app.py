"""
Application Flask de gestion de stock.
Gère les produits, mouvements de stock et génère des alertes.

Routes principales:
  - /                      : Dashboard
  - /product/add           : Ajouter un produit
  - /product/<id>          : Détails du produit
  - /product/<id>/movement : Enregistrer un mouvement
  - /product/<id>/edit     : Modifier un produit
  - /product/<id>/delete   : Supprimer un produit
  - /movements             : Historique des mouvements
  - /alerts                : Gestion des alertes
"""

import os
from datetime import datetime, timedelta
from flask import Flask, render_template, request, redirect, url_for, flash, jsonify

from models import db, Product, Movement, Alert, check_and_create_alert


# ==========================================
# Configuration Flask et Base de données
# ==========================================

basedir = os.path.abspath(os.path.dirname(__file__))

app = Flask(__name__)

# Configuration
app.config['SECRET_KEY'] = 'votre_cle_secrete_tres_securisee_12345'
app.config['SQLALCHEMY_DATABASE_URI'] = (
    'sqlite:///' + os.path.join(basedir, 'inventory.db')
)
app.config['SQLALCHEMY_TRACK_MODIFICATIONS'] = False

# Initialisation de la base de données
db.init_app(app)

# Création des tables au démarrage
with app.app_context():
    db.create_all()


# ==========================================
# Fonctions utilitaires
# ==========================================

def get_dashboard_stats():
    """
    Calcule les statistiques du dashboard.
    
    Returns:
        dict: Dictionnaire contenant les KPIs
    """
    products = Product.query.all()
    
    total_products = len(products)
    total_stock = sum(p.current_stock for p in products)
    critical_products = sum(1 for p in products if p.is_critical)
    low_stock_products = sum(1 for p in products if p.is_low)
    
    # Mouvements du dernier mois
    thirty_days_ago = datetime.utcnow() - timedelta(days=30)
    recent_movements = Movement.query.filter(
        Movement.timestamp >= thirty_days_ago
    ).count()
    
    # Alertes actives
    active_alerts = Alert.query.filter_by(is_resolved=False).count()
    
    return {
        'total_products': total_products,
        'total_stock': total_stock,
        'critical_products': critical_products,
        'low_stock_products': low_stock_products,
        'recent_movements': recent_movements,
        'active_alerts': active_alerts,
    }


# ==========================================
# Routes - Dashboard
# ==========================================

@app.route('/')
def dashboard():
    """
    Affiche le dashboard principal avec les KPIs et liste des produits.
    """
    products = Product.query.all()
    alerts = Alert.query.filter_by(is_resolved=False).order_by(
        Alert.created_at.desc()
    ).all()
    stats = get_dashboard_stats()
    
    return render_template(
        'index.html',
        products=products,
        alerts=alerts,
        stats=stats
    )


# ==========================================
# Routes - Gestion des Produits (CRUD)
# ==========================================

@app.route('/product/add', methods=['GET', 'POST'])
def add_product():
    """
    Ajoute un nouveau produit.
    GET:  Affiche le formulaire
    POST: Crée le produit en base
    """
    if request.method == 'POST':
        try:
            # Récupération et validation des données
            name = request.form.get('name', '').strip()
            sku = request.form.get('sku', '').strip()
            description = request.form.get('description', '').strip()
            initial_stock = int(request.form.get('initial_stock', 0))
            threshold = int(request.form.get('threshold', 10))
            
            # Validation
            if not name or not sku:
                flash('Le nom et le SKU sont obligatoires.', 'danger')
                return redirect(url_for('add_product'))
            
            # Vérifier si le produit existe déjà
            existing = Product.query.filter(
                (Product.name == name) | (Product.sku == sku)
            ).first()
            
            if existing:
                flash('Un produit avec ce nom ou SKU existe déjà.', 'danger')
                return redirect(url_for('add_product'))
            
            # Création du produit
            product = Product(
                name=name,
                sku=sku,
                description=description,
                current_stock=initial_stock,
                threshold=threshold
            )
            
            db.session.add(product)
            db.session.commit()
            
            # Créer une alerte si le stock initial est faible
            check_and_create_alert(product)
            db.session.commit()
            
            flash(f'✅ Produit "{product.name}" ajouté avec succès.', 'success')
            return redirect(url_for('dashboard'))
            
        except ValueError:
            flash('Les valeurs numériques sont invalides.', 'danger')
            return redirect(url_for('add_product'))
        except Exception as e:
            db.session.rollback()
            flash(f'❌ Erreur: {str(e)}', 'danger')
            return redirect(url_for('add_product'))
    
    return render_template('add_product.html')


@app.route('/product/<int:product_id>')
def view_product(product_id):
    """
    Affiche les détails d'un produit avec son historique et ses alertes.
    """
    product = Product.query.get_or_404(product_id)
    
    # Récupérer les 20 derniers mouvements
    movements = Movement.query.filter_by(
        product_id=product_id
    ).order_by(Movement.timestamp.desc()).limit(20).all()
    
    # Récupérer les alertes associées
    alerts = Alert.query.filter_by(product_id=product_id).order_by(
        Alert.created_at.desc()
    ).all()
    
    return render_template(
        'view_product.html',
        product=product,
        movements=movements,
        alerts=alerts
    )


@app.route('/product/<int:product_id>/edit', methods=['GET', 'POST'])
def edit_product(product_id):
    """
    Modifie un produit existant.
    """
    product = Product.query.get_or_404(product_id)
    
    if request.method == 'POST':
        try:
            product.name = request.form.get('name', '').strip()
            product.description = request.form.get('description', '').strip()
            product.threshold = int(request.form.get('threshold', 10))
            product.updated_at = datetime.utcnow()
            
            if not product.name:
                flash('Le nom du produit est obligatoire.', 'danger')
                return redirect(url_for('edit_product', product_id=product_id))
            
            db.session.commit()
            
            # Vérifier les alertes après modification du seuil
            check_and_create_alert(product)
            db.session.commit()
            
            flash(f'✅ Produit "{product.name}" modifié avec succès.', 'success')
            return redirect(url_for('view_product', product_id=product_id))
            
        except ValueError:
            flash('Les valeurs numériques sont invalides.', 'danger')
            return redirect(url_for('edit_product', product_id=product_id))
        except Exception as e:
            db.session.rollback()
            flash(f'❌ Erreur: {str(e)}', 'danger')
            return redirect(url_for('edit_product', product_id=product_id))
    
    return render_template('edit_product.html', product=product)


@app.route('/product/<int:product_id>/delete')
def delete_product(product_id):
    """
    Supprime un produit et tous ses mouvements/alertes.
    """
    product = Product.query.get_or_404(product_id)
    product_name = product.name
    
    try:
        # Les cascades suppriment automatiquement les mouvements et alertes
        db.session.delete(product)
        db.session.commit()
        flash(f'✅ Produit "{product_name}" supprimé avec succès.', 'success')
    except Exception as e:
        db.session.rollback()
        flash(f'❌ Erreur lors de la suppression: {str(e)}', 'danger')
    
    return redirect(url_for('dashboard'))


# ==========================================
# Routes - Gestion des Mouvements de Stock
# ==========================================

@app.route('/product/<int:product_id>/movement', methods=['GET', 'POST'])
def add_movement(product_id):
    """
    Enregistre un mouvement de stock (entrée ou sortie).
    """
    product = Product.query.get_or_404(product_id)
    
    if request.method == 'POST':
        try:
            quantity = int(request.form.get('quantity', 0))
            movement_type = request.form.get('movement_type', 'IN')
            reason = request.form.get('reason', '').strip()
            
            # Validation
            if quantity <= 0:
                flash('La quantité doit être positive.', 'danger')
                return redirect(url_for('add_movement', product_id=product_id))
            
            if movement_type not in ['IN', 'OUT']:
                flash('Type de mouvement invalide.', 'danger')
                return redirect(url_for('add_movement', product_id=product_id))
            
            # Vérifier si la sortie ne rend pas le stock négatif (optionnel)
            if movement_type == 'OUT' and (product.current_stock - quantity) < 0:
                flash(f'⚠️ Attention: La quantité demandée dépasse le stock disponible ({product.current_stock}).', 'warning')
            
            # Créer le mouvement
            quantity_change = quantity if movement_type == 'IN' else -quantity
            
            movement = Movement(
                product_id=product_id,
                quantity_change=quantity_change,
                movement_type=movement_type,
                reason=reason if reason else ('Entrée' if movement_type == 'IN' else 'Sortie'),
                recorded_by='Admin'
            )
            
            # Mettre à jour le stock
            product.current_stock += quantity_change
            product.updated_at = datetime.utcnow()
            
            db.session.add(movement)
            
            # Vérifier et créer une alerte si nécessaire
            check_and_create_alert(product)
            
            db.session.commit()
            
            msg = f"Entrée de {quantity} unités" if movement_type == 'IN' else f"Sortie de {quantity} unités"
            flash(f'✅ Mouvement enregistré: {msg}', 'success')
            
            return redirect(url_for('view_product', product_id=product_id))
            
        except ValueError:
            flash('Les valeurs numériques sont invalides.', 'danger')
            return redirect(url_for('add_movement', product_id=product_id))
        except Exception as e:
            db.session.rollback()
            flash(f'❌ Erreur: {str(e)}', 'danger')
            return redirect(url_for('add_movement', product_id=product_id))
    
    return render_template('movement.html', product=product)


# ==========================================
# Routes - Historique et Alertes
# ==========================================

@app.route('/movements')
def movements_history():
    """
    Affiche l'historique de tous les mouvements de stock.
    """
    # Récupérer les paramètres de pagination
    page = request.args.get('page', 1, type=int)
    product_id = request.args.get('product_id', None, type=int)
    
    query = Movement.query
    
    if product_id:
        query = query.filter_by(product_id=product_id)
    
    movements = query.order_by(Movement.timestamp.desc()).paginate(
        page=page, per_page=20
    )
    
    products = Product.query.all()
    
    return render_template(
        'movements.html',
        movements=movements,
        products=products,
        selected_product_id=product_id
    )


@app.route('/alerts')
def view_alerts():
    """
    Affiche la page de gestion des alertes.
    """
    # Récupérer les alertes (résolues et non résolues)
    active_alerts = Alert.query.filter_by(
        is_resolved=False
    ).order_by(Alert.created_at.desc()).all()
    
    resolved_alerts = Alert.query.filter_by(
        is_resolved=True
    ).order_by(Alert.created_at.desc()).limit(10).all()
    
    return render_template(
        'alerts.html',
        active_alerts=active_alerts,
        resolved_alerts=resolved_alerts
    )


@app.route('/alert/<int:alert_id>/resolve')
def resolve_alert(alert_id):
    """
    Marque une alerte comme résolue.
    """
    alert = Alert.query.get_or_404(alert_id)
    
    try:
        alert.is_resolved = True
        db.session.commit()
        flash('✅ Alerte marquée comme résolue.', 'success')
    except Exception as e:
        db.session.rollback()
        flash(f'❌ Erreur: {str(e)}', 'danger')
    
    return redirect(url_for('view_alerts'))


# ==========================================
# Routes - API (optionnel)
# ==========================================

@app.route('/api/dashboard/stats')
def api_dashboard_stats():
    """
    Retourne les statistiques du dashboard au format JSON.
    Utile pour des mises à jour AJAX.
    """
    stats = get_dashboard_stats()
    return jsonify(stats)


@app.route('/api/product/<int:product_id>/stock')
def api_product_stock(product_id):
    """
    Retourne les informations de stock d'un produit au format JSON.
    """
    product = Product.query.get_or_404(product_id)
    return jsonify({
        'id': product.id,
        'name': product.name,
        'current_stock': product.current_stock,
        'threshold': product.threshold,
        'status': product.status,
        'is_critical': product.is_critical,
        'is_low': product.is_low
    })


# ==========================================
# Gestion des erreurs
# ==========================================

@app.errorhandler(404)
def page_not_found(error):
    """Gère les erreurs 404."""
    return render_template('404.html'), 404


@app.errorhandler(500)
def internal_error(error):
    """Gère les erreurs 500."""
    db.session.rollback()
    return render_template('500.html'), 500


# ==========================================
# Démarrage de l'application
# ==========================================

if __name__ == '__main__':
    # debug=True pour le développement, mettre à False en production
    app.run(debug=True, host='127.0.0.1', port=5000)