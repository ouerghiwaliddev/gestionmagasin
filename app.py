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

import json
import os
from functools import wraps
from datetime import datetime, timedelta
from decimal import Decimal, InvalidOperation
from flask import Flask, abort, flash, jsonify, redirect, render_template, request, send_file, url_for
from flask_login import LoginManager, current_user, login_required, login_user, logout_user
from flask_wtf.csrf import CSRFProtect
from sqlalchemy import func, inspect, or_, text
from dotenv import load_dotenv

from export_utils import build_excel, build_pdf
from models import AuditLog, User, db, Product, Movement, Alert, check_and_create_alert


# ==========================================
# Configuration Flask et Base de données
# ==========================================

basedir = os.path.abspath(os.path.dirname(__file__))
load_dotenv(os.path.join(basedir, '.env'))

app = Flask(__name__)

# Configuration
app.config['SECRET_KEY'] = os.environ.get('SECRET_KEY')
if not app.config['SECRET_KEY']:
    raise RuntimeError("La variable d'environnement SECRET_KEY est obligatoire.")
app.config['SQLALCHEMY_DATABASE_URI'] = os.environ.get(
    'DATABASE_URL', 'sqlite:///' + os.path.join(basedir, 'inventory.db')
)
app.config['SQLALCHEMY_TRACK_MODIFICATIONS'] = False

# Initialisation de la base de données
db.init_app(app)
csrf = CSRFProtect(app)
login_manager = LoginManager(app)
login_manager.login_view = 'login'
login_manager.login_message = 'Veuillez vous connecter pour continuer.'
login_manager.login_message_category = 'warning'


@login_manager.user_loader
def load_user(user_id):
    return db.session.get(User, int(user_id))


@login_manager.unauthorized_handler
def unauthorized():
    if request.path.startswith('/api/'):
        return jsonify({'error': 'Authentification requise'}), 401
    return redirect(url_for('login', next=request.full_path if request.query_string else request.path))


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

def bootstrap_admin():
    """Cree le premier administrateur a partir de l'environnement."""
    if User.query.count() == 0:
        admin_username = os.environ.get('ADMIN_USERNAME', '').strip()
        admin_password = os.environ.get('ADMIN_PASSWORD', '')
        if not admin_username or len(admin_password) < 8:
            raise RuntimeError(
                'Aucun utilisateur: ADMIN_USERNAME et ADMIN_PASSWORD '
                '(8 caracteres minimum) sont obligatoires.'
            )
        admin = User(
            username=admin_username,
            display_name=os.environ.get('ADMIN_DISPLAY_NAME', admin_username).strip() or admin_username,
            role='admin',
            active=True,
        )
        admin.set_password(admin_password)
        db.session.add(admin)
        db.session.commit()


# Création des tables au démarrage
with app.app_context():
    db.create_all()
    ensure_movement_columns()
    bootstrap_admin()


# ==========================================
# Fonctions utilitaires
# ==========================================

PERIODS = {
    'always': ('Tous', None),
    'yearly': ('Annuel', 365),
    'quarterly': ('Trimestriel', 90),
    'monthly': ('Mensuel', 30),
}


def admin_required(view):
    @wraps(view)
    def wrapped(*args, **kwargs):
        if not current_user.is_admin:
            abort(403)
        return view(*args, **kwargs)
    return wrapped


def json_value(value):
    if isinstance(value, (datetime, Decimal)):
        return str(value)
    return value


def audit(action, target_type, target_id=None, before=None, after=None, actor=None):
    """Ajoute une entree d'audit a la transaction courante."""
    actor = actor or (current_user if current_user.is_authenticated else None)
    entry = AuditLog(
        actor_id=actor.id if actor else None,
        actor_username=actor.username if actor else 'system',
        action=action,
        target_type=target_type,
        target_id=str(target_id) if target_id is not None else None,
        details=json.dumps(
            {'before': before, 'after': after},
            ensure_ascii=False,
            default=json_value,
        ),
    )
    db.session.add(entry)
    return entry


def product_snapshot(product):
    return {
        'name': product.name,
        'sku': product.sku,
        'description': product.description,
        'current_stock': product.current_stock,
        'threshold': product.threshold,
    }


def user_snapshot(user):
    return {
        'username': user.username,
        'display_name': user.display_name,
        'role': user.role,
        'active': user.active,
    }


def period_context(period=None):
    key = period if period in PERIODS else current_user.dashboard_period
    if key not in PERIODS:
        key = 'always'
    label, days = PERIODS[key]
    end = datetime.utcnow()
    start = end - timedelta(days=days) if days else None
    return key, label, start, end


def get_dashboard_stats(period=None):
    """
    Calcule les statistiques du dashboard.
    
    Returns:
        dict: Dictionnaire contenant les KPIs
    """
    products = Product.query.all()
    
    total_products = len(products)
    critical_products = sum(1 for p in products if p.is_critical)
    low_stock_products = sum(1 for p in products if p.is_low)

    period_key, period_label, period_start, period_end = period_context(period)
    activity_query = Movement.query
    if period_start:
        activity_query = activity_query.filter(Movement.timestamp >= period_start)
    movements = activity_query.filter(Movement.timestamp <= period_end).all()
    entries = [movement for movement in movements if movement.is_entry]
    exits = [movement for movement in movements if movement.is_exit]
    
    # Alertes actives
    active_alerts = Alert.query.filter_by(is_resolved=False).count()
    
    return {
        'total_products': total_products,
        'critical_products': critical_products,
        'low_stock_products': low_stock_products,
        'recent_movements': len(movements),
        'entry_count': len(entries),
        'entry_quantity': sum(m.quantity_change for m in entries),
        'entry_value': sum((m.total_value for m in entries), Decimal('0')),
        'exit_count': len(exits),
        'exit_quantity': sum(abs(m.quantity_change) for m in exits),
        'exit_value': sum((m.total_value for m in exits), Decimal('0')),
        'active_alerts': active_alerts,
        'period': period_key,
        'period_label': period_label,
        'period_start': period_start,
        'period_end': period_end,
    }


@app.before_request
def require_authentication():
    if request.endpoint in {'login', 'static'} or request.endpoint is None:
        return None
    if current_user.is_authenticated and not current_user.active:
        logout_user()
    if not current_user.is_authenticated:
        return unauthorized()
    return None


@app.route('/login', methods=['GET', 'POST'])
def login():
    if current_user.is_authenticated:
        return redirect(url_for('dashboard'))
    if request.method == 'POST':
        username = request.form.get('username', '').strip()
        password = request.form.get('password', '')
        user = User.query.filter(func.lower(User.username) == username.lower()).first()
        if not user or not user.active or not user.check_password(password):
            flash('Identifiants invalides ou compte désactivé.', 'danger')
            return render_template('login.html'), 401
        login_user(user)
        user.last_login_at = datetime.utcnow()
        audit('LOGIN', 'session', actor=user, after={'username': user.username})
        db.session.commit()
        next_url = request.args.get('next', '')
        return redirect(next_url if next_url.startswith('/') and not next_url.startswith('//') else url_for('dashboard'))
    return render_template('login.html')


@app.route('/logout', methods=['POST'])
def logout():
    user = current_user._get_current_object()
    audit('LOGOUT', 'session', actor=user, before={'username': user.username})
    db.session.commit()
    logout_user()
    return redirect(url_for('login'))


@app.route('/account/password', methods=['GET', 'POST'])
def change_password():
    if request.method == 'POST':
        current_password = request.form.get('current_password', '')
        new_password = request.form.get('new_password', '')
        confirmation = request.form.get('password_confirmation', '')
        if not current_user.check_password(current_password):
            flash('Le mot de passe actuel est incorrect.', 'danger')
        elif len(new_password) < 8:
            flash('Le nouveau mot de passe doit contenir au moins 8 caractères.', 'danger')
        elif new_password != confirmation:
            flash('La confirmation du mot de passe ne correspond pas.', 'danger')
        else:
            current_user.set_password(new_password)
            audit('PASSWORD_CHANGE', 'user', current_user.id)
            db.session.commit()
            flash('Mot de passe modifié.', 'success')
            return redirect(url_for('dashboard'))
    return render_template('change_password.html')


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
    stats = get_dashboard_stats(current_user.dashboard_period)
    
    return render_template(
        'index.html',
        products=products,
        alerts=alerts,
        stats=stats
    )


@app.route('/dashboard/period', methods=['POST'])
def set_dashboard_period():
    period = request.form.get('period', '')
    if period not in PERIODS:
        abort(400)
    previous = current_user.dashboard_period
    current_user.dashboard_period = period
    audit(
        'DASHBOARD_PERIOD_CHANGE', 'user', current_user.id,
        before={'dashboard_period': previous}, after={'dashboard_period': period},
    )
    db.session.commit()
    return redirect(url_for('dashboard'))


# ==========================================
# Routes - Administration des utilisateurs
# ==========================================

@app.route('/admin/users')
@admin_required
def users_admin():
    users = User.query.order_by(User.username).all()
    return render_template('users.html', users=users)


@app.route('/admin/users/create', methods=['POST'])
@admin_required
def create_user():
    username = request.form.get('username', '').strip()
    display_name = request.form.get('display_name', '').strip()
    role = request.form.get('role', '')
    password = request.form.get('password', '')
    if not username or not display_name or role not in {'admin', 'manager'}:
        flash('Les informations du compte sont invalides.', 'danger')
    elif len(password) < 8:
        flash('Le mot de passe doit contenir au moins 8 caractères.', 'danger')
    elif User.query.filter(func.lower(User.username) == username.lower()).first():
        flash('Ce nom d’utilisateur existe déjà.', 'danger')
    else:
        user = User(username=username, display_name=display_name, role=role, active=True)
        user.set_password(password)
        db.session.add(user)
        db.session.flush()
        audit('USER_CREATE', 'user', user.id, after=user_snapshot(user))
        db.session.commit()
        flash('Compte créé.', 'success')
    return redirect(url_for('users_admin'))


@app.route('/admin/users/<int:user_id>/edit', methods=['POST'])
@admin_required
def edit_user(user_id):
    user = User.query.get_or_404(user_id)
    display_name = request.form.get('display_name', '').strip()
    role = request.form.get('role', '')
    if not display_name or role not in {'admin', 'manager'}:
        flash('Les informations du compte sont invalides.', 'danger')
        return redirect(url_for('users_admin'))
    if user.id == current_user.id and role != 'admin':
        flash('Vous ne pouvez pas retirer votre propre rôle administrateur.', 'danger')
        return redirect(url_for('users_admin'))
    if user.role == 'admin' and role != 'admin' and user.active:
        active_admins = User.query.filter_by(role='admin', active=True).count()
        if active_admins <= 1:
            flash('Le dernier administrateur actif doit être conservé.', 'danger')
            return redirect(url_for('users_admin'))
    before = user_snapshot(user)
    user.display_name = display_name
    user.role = role
    audit('USER_UPDATE', 'user', user.id, before=before, after=user_snapshot(user))
    db.session.commit()
    flash('Compte modifié.', 'success')
    return redirect(url_for('users_admin'))


@app.route('/admin/users/<int:user_id>/reset-password', methods=['POST'])
@admin_required
def reset_user_password(user_id):
    user = User.query.get_or_404(user_id)
    password = request.form.get('password', '')
    if len(password) < 8:
        flash('Le mot de passe doit contenir au moins 8 caractères.', 'danger')
    else:
        user.set_password(password)
        audit('PASSWORD_RESET', 'user', user.id, after={'username': user.username})
        db.session.commit()
        flash('Mot de passe réinitialisé.', 'success')
    return redirect(url_for('users_admin'))


@app.route('/admin/users/<int:user_id>/deactivate', methods=['POST'])
@admin_required
def deactivate_user(user_id):
    user = User.query.get_or_404(user_id)
    if user.id == current_user.id:
        flash('Vous ne pouvez pas désactiver votre propre compte.', 'danger')
    elif not user.active:
        flash('Ce compte est déjà désactivé.', 'warning')
    elif user.role == 'admin' and User.query.filter_by(role='admin', active=True).count() <= 1:
        flash('Le dernier administrateur actif doit être conservé.', 'danger')
    else:
        before = user_snapshot(user)
        user.active = False
        audit('USER_DEACTIVATE', 'user', user.id, before=before, after=user_snapshot(user))
        db.session.commit()
        flash('Compte désactivé.', 'success')
    return redirect(url_for('users_admin'))


@app.route('/admin/users/<int:user_id>/activate', methods=['POST'])
@admin_required
def activate_user(user_id):
    user = User.query.get_or_404(user_id)
    before = user_snapshot(user)
    user.active = True
    audit('USER_ACTIVATE', 'user', user.id, before=before, after=user_snapshot(user))
    db.session.commit()
    flash('Compte activé.', 'success')
    return redirect(url_for('users_admin'))


@app.route('/admin/audit')
@admin_required
def audit_history():
    page = request.args.get('page', 1, type=int)
    actor_id = request.args.get('actor_id', type=int)
    action = request.args.get('action', '').strip()
    date_from = request.args.get('date_from', '').strip()
    date_to = request.args.get('date_to', '').strip()
    query = AuditLog.query
    if actor_id:
        query = query.filter(AuditLog.actor_id == actor_id)
    if action:
        query = query.filter(AuditLog.action == action)
    try:
        if date_from:
            query = query.filter(AuditLog.created_at >= datetime.strptime(date_from, '%Y-%m-%d'))
        if date_to:
            query = query.filter(
                AuditLog.created_at < datetime.strptime(date_to, '%Y-%m-%d') + timedelta(days=1)
            )
    except ValueError:
        flash('La plage de dates est invalide.', 'danger')
        query = query.filter(False)
    logs = query.order_by(AuditLog.created_at.desc()).paginate(page=page, per_page=30)
    users = User.query.order_by(User.username).all()
    actions = [row[0] for row in db.session.query(AuditLog.action).distinct().order_by(AuditLog.action)]
    filters = {'actor_id': actor_id or '', 'action': action, 'date_from': date_from, 'date_to': date_to}
    return render_template('audit.html', logs=logs, users=users, actions=actions, filters=filters)


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
            db.session.flush()
            
            # Créer une alerte si le stock initial est faible
            check_and_create_alert(product)
            audit('PRODUCT_CREATE', 'product', product.id, after=product_snapshot(product))
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
            before = product_snapshot(product)
            product.name = request.form.get('name', '').strip()
            product.description = request.form.get('description', '').strip()
            product.threshold = int(request.form.get('threshold', 10))
            product.updated_at = datetime.utcnow()
            
            if not product.name:
                flash('Le nom du produit est obligatoire.', 'danger')
                return redirect(url_for('edit_product', product_id=product_id))
            
            # Vérifier les alertes après modification du seuil
            check_and_create_alert(product)
            audit(
                'PRODUCT_UPDATE', 'product', product.id,
                before=before, after=product_snapshot(product),
            )
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


@app.route('/product/<int:product_id>/delete', methods=['POST'])
def delete_product(product_id):
    """
    Supprime un produit et tous ses mouvements/alertes.
    """
    product = Product.query.get_or_404(product_id)
    product_name = product.name
    before = product_snapshot(product)
    
    try:
        # Les cascades suppriment automatiquement les mouvements et alertes
        audit('PRODUCT_DELETE', 'product', product.id, before=before)
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
                recorded_by=current_user.username
            )
            
            # Mettre à jour le stock
            product.current_stock += quantity_change
            product.updated_at = datetime.utcnow()
            
            db.session.add(movement)
            db.session.flush()
            
            # Vérifier et créer une alerte si nécessaire
            check_and_create_alert(product)
            audit(
                'STOCK_MOVEMENT', 'movement', movement.id,
                after={
                    'product_id': product.id,
                    'product': product.name,
                    'type': movement_type,
                    'quantity': quantity,
                    'unit_price': unit_price,
                    'stock_after': product.current_stock,
                    'reason': movement.reason,
                },
            )
            
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
    stats = get_dashboard_stats(current_user.dashboard_period)
    sections = [
        {
            'title': 'Indicateurs',
            'headers': ('Indicateur', 'Valeur'),
            'rows': [
                ('Produits', stats['total_products']),
                ('Ruptures de stock', stats['critical_products']),
                ('Stocks faibles', stats['low_stock_products']),
                (f"Mouvements - {stats['period_label']}", stats['recent_movements']),
                ("Nombre d'entrées", stats['entry_count']),
                ('Quantités entrées', stats['entry_quantity']),
                ('Valeur des entrées (TND)', f"{stats['entry_value']:.2f}"),
                ('Nombre de sorties', stats['exit_count']),
                ('Quantités sorties', stats['exit_quantity']),
                ('Valeur des sorties (TND)', f"{stats['exit_value']:.2f}"),
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


@app.route('/alert/<int:alert_id>/resolve', methods=['POST'])
def resolve_alert(alert_id):
    """
    Marque une alerte comme résolue.
    """
    alert = Alert.query.get_or_404(alert_id)
    
    try:
        before = {'is_resolved': alert.is_resolved, 'message': alert.message}
        alert.is_resolved = True
        audit(
            'ALERT_RESOLVE', 'alert', alert.id,
            before=before, after={'is_resolved': True, 'message': alert.message},
        )
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
    stats = get_dashboard_stats(current_user.dashboard_period)
    return jsonify({
        **stats,
        'entry_value': float(stats['entry_value']),
        'exit_value': float(stats['exit_value']),
        'period_start': stats['period_start'].isoformat() if stats['period_start'] else None,
        'period_end': stats['period_end'].isoformat(),
    })


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

@app.errorhandler(403)
def forbidden(error):
    """Affiche une erreur claire lorsque le role est insuffisant."""
    return render_template('403.html'), 403


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
