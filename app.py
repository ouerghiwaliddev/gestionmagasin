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
from decimal import Decimal, InvalidOperation
from flask import Flask, abort, flash, jsonify, redirect, render_template, request, send_file, url_for
from sqlalchemy import func, inspect, or_, text

from export_utils import build_excel, build_pdf
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


def ensure_movement_columns():
    """Ajoute les nouveaux champs aux bases SQLite creees avant cette version."""
    existing_columns = {
        column['name'] for column in inspect(db.engine).get_columns('movement')
    }
    columns = {
        'unit_price': "NUMERIC(12, 2) NOT NULL DEFAULT 0",
        'source_direction': "VARCHAR(120) NOT NULL DEFAULT ''",
        'source_agent': "VARCHAR(120) NOT NULL DEFAULT ''",
        'destination_direction': "VARCHAR(120) NOT NULL DEFAULT ''",
        'destination_agent': "VARCHAR(120) NOT NULL DEFAULT ''",
    }
    for name, definition in columns.items():
        if name not in existing_columns:
            db.session.execute(text(
                f'ALTER TABLE movement ADD COLUMN {name} {definition}'
            ))
    db.session.commit()

# Création des tables au démarrage
with app.app_context():
    db.create_all()
    ensure_movement_columns()


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
            unit_price_raw = request.form.get('unit_price', '').strip()
            reason = request.form.get('reason', '').strip()
            source_direction = request.form.get('source_direction', '').strip()
            source_agent = request.form.get('source_agent', '').strip()
            destination_direction = request.form.get('destination_direction', '').strip()
            destination_agent = request.form.get('destination_agent', '').strip()
            
            # Validation
            if quantity <= 0:
                flash('La quantité doit être positive.', 'danger')
                return redirect(url_for('add_movement', product_id=product_id))

            if movement_type not in ['IN', 'OUT']:
                flash('Type de mouvement invalide.', 'danger')
                return redirect(url_for('add_movement', product_id=product_id))

            if movement_type == 'IN' and not unit_price_raw:
                flash("Le prix unitaire est obligatoire pour une entrée.", 'danger')
                return redirect(url_for('add_movement', product_id=product_id))

            unit_price = Decimal(unit_price_raw or '0')
            if not unit_price.is_finite() or unit_price < 0:
                flash('Le prix unitaire ne peut pas être négatif.', 'danger')
                return redirect(url_for('add_movement', product_id=product_id))

            if not all((source_direction, source_agent, destination_direction, destination_agent)):
                flash('Les directions et agents source et destination sont obligatoires.', 'danger')
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
                unit_price=unit_price,
                source_direction=source_direction,
                source_agent=source_agent,
                destination_direction=destination_direction,
                destination_agent=destination_agent,
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
            
        except (ValueError, InvalidOperation):
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

def filtered_movements_query():
    """Retourne la requête des mouvements avec les filtres HTTP actifs."""
    product_id = request.args.get('product_id', None, type=int)
    date_from = request.args.get('date_from', '').strip()
    date_to = request.args.get('date_to', '').strip()
    direction = request.args.get('direction', '').strip()
    agent = request.args.get('agent', '').strip()
    
    query = Movement.query
    
    if product_id:
        query = query.filter_by(product_id=product_id)

    try:
        start_date = (
            datetime.strptime(date_from, '%Y-%m-%d').date()
            if date_from else None
        )
        end_date = (
            datetime.strptime(date_to, '%Y-%m-%d').date()
            if date_to else None
        )

        if start_date and end_date and start_date > end_date:
            flash('La date de début doit précéder la date de fin.', 'danger')
            query = query.filter(False)
        else:
            if start_date:
                query = query.filter(func.date(Movement.timestamp) >= start_date)
            if end_date:
                query = query.filter(func.date(Movement.timestamp) <= end_date)
    except ValueError:
        flash('La plage de dates est invalide.', 'danger')
        query = query.filter(False)

    if direction:
        pattern = f'%{direction}%'
        query = query.filter(or_(
            Movement.source_direction.ilike(pattern),
            Movement.destination_direction.ilike(pattern)
        ))

    if agent:
        pattern = f'%{agent}%'
        query = query.filter(or_(
            Movement.source_agent.ilike(pattern),
            Movement.destination_agent.ilike(pattern)
        ))
    
    filters = {
        'product_id': product_id or '',
        'date_from': date_from,
        'date_to': date_to,
        'direction': direction,
        'agent': agent,
    }
    return query, filters


@app.route('/movements')
def movements_history():
    """Affiche l'historique de tous les mouvements de stock."""
    page = request.args.get('page', 1, type=int)
    query, filters = filtered_movements_query()
    movements = query.order_by(Movement.timestamp.desc()).paginate(
        page=page, per_page=20
    )
    products = Product.query.order_by(Product.name).all()

    return render_template(
        'movements.html',
        movements=movements,
        products=products,
        selected_product_id=request.args.get('product_id', None, type=int),
        filters=filters
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


def format_datetime(value):
    """Formate une date pour les rapports."""
    return value.strftime('%d/%m/%Y %H:%M') if value else ''


def product_rows(products):
    return [
        (
            product.sku, product.name, product.description or '',
            product.current_stock, product.threshold, product.status,
            format_datetime(product.created_at), format_datetime(product.updated_at),
        )
        for product in products
    ]


def movement_rows(movements):
    rows = []
    for movement in movements:
        unit_price = getattr(movement, 'unit_price', 0) or 0
        rows.append((
            movement.product.sku,
            movement.product.name,
            'Entrée' if movement.movement_type == 'IN' else 'Sortie',
            movement.quantity_change,
            f'{unit_price:.2f}',
            f'{abs(movement.quantity_change) * unit_price:.2f}',
            getattr(movement, 'source_direction', '') or '',
            getattr(movement, 'source_agent', '') or '',
            getattr(movement, 'destination_direction', '') or '',
            getattr(movement, 'destination_agent', '') or '',
            movement.reason or '',
            movement.recorded_by or '',
            format_datetime(movement.timestamp),
        ))
    return rows


def alert_rows(alerts):
    return [
        (
            alert.product.sku, alert.product.name, alert.alert_type,
            alert.message, 'Résolue' if alert.is_resolved else 'Active',
            format_datetime(alert.created_at),
        )
        for alert in alerts
    ]


PRODUCT_HEADERS = (
    'SKU', 'Produit', 'Description', 'Stock', 'Seuil', 'État', 'Créé le', 'Modifié le'
)
MOVEMENT_HEADERS = (
    'SKU', 'Produit', 'Type', 'Quantité', 'Prix unitaire (TND)', 'Valeur (TND)',
    'Direction source', 'Agent source', 'Direction destination', 'Agent destination',
    'Motif', 'Enregistré par', 'Date',
)
ALERT_HEADERS = ('SKU', 'Produit', 'Type', 'Message', 'État', 'Date')


def send_export(sections, filename, file_format):
    """Envoie un rapport dans le format demandé."""
    if file_format == 'excel':
        return send_file(
            build_excel(sections),
            mimetype='application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
            as_attachment=True,
            download_name=f'{filename}.xlsx',
        )
    if file_format == 'pdf':
        return send_file(
            build_pdf(sections),
            mimetype='application/pdf',
            as_attachment=True,
            download_name=f'{filename}.pdf',
        )
    abort(404)


@app.route('/export/dashboard/<file_format>')
def export_dashboard(file_format):
    products = Product.query.order_by(Product.name).all()
    alerts = Alert.query.filter_by(is_resolved=False).order_by(Alert.created_at.desc()).all()
    stats = get_dashboard_stats()
    sections = [
        {
            'title': 'Indicateurs',
            'headers': ('Indicateur', 'Valeur'),
            'rows': [
                ('Produits', stats['total_products']),
                ('Unités en stock', stats['total_stock']),
                ('Ruptures de stock', stats['critical_products']),
                ('Stocks faibles', stats['low_stock_products']),
                ('Mouvements sur 30 jours', stats['recent_movements']),
                ('Alertes actives', stats['active_alerts']),
            ],
        },
        {'title': 'Produits', 'headers': PRODUCT_HEADERS, 'rows': product_rows(products)},
        {'title': 'Alertes actives', 'headers': ALERT_HEADERS, 'rows': alert_rows(alerts)},
    ]
    return send_export(sections, 'tableau_de_bord', file_format)


@app.route('/export/movements/<file_format>')
def export_movements(file_format):
    query, _filters = filtered_movements_query()
    movements = query.order_by(Movement.timestamp.desc()).all()
    sections = [{
        'title': 'Mouvements de stock',
        'headers': MOVEMENT_HEADERS,
        'rows': movement_rows(movements),
    }]
    return send_export(sections, 'mouvements', file_format)


@app.route('/export/alerts/<file_format>')
def export_alerts(file_format):
    alerts = Alert.query.order_by(Alert.created_at.desc()).all()
    sections = [{
        'title': 'Alertes',
        'headers': ALERT_HEADERS,
        'rows': alert_rows(alerts),
    }]
    return send_export(sections, 'alertes', file_format)


@app.route('/product/<int:product_id>/export/<file_format>')
def export_product(product_id, file_format):
    product = Product.query.get_or_404(product_id)
    movements = Movement.query.filter_by(product_id=product_id).order_by(Movement.timestamp.desc()).all()
    alerts = Alert.query.filter_by(product_id=product_id).order_by(Alert.created_at.desc()).all()
    sections = [
        {'title': 'Produit', 'headers': PRODUCT_HEADERS, 'rows': product_rows([product])},
        {'title': 'Mouvements', 'headers': MOVEMENT_HEADERS, 'rows': movement_rows(movements)},
        {'title': 'Alertes', 'headers': ALERT_HEADERS, 'rows': alert_rows(alerts)},
    ]
    return send_export(sections, f'produit_{product.sku}', file_format)


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
    app.run(debug=True, host='0.0.0.0', port=5016)
