"""
Modèles SQLAlchemy pour l'application de gestion de stock.
Gère les produits, mouvements de stock et alertes.
"""

import json

from flask_login import UserMixin
from flask_sqlalchemy import SQLAlchemy
from werkzeug.security import check_password_hash, generate_password_hash
from datetime import datetime

db = SQLAlchemy()


class User(UserMixin, db.Model):
    """Compte authentifie de l'application."""
    __tablename__ = 'user'

    id = db.Column(db.Integer, primary_key=True)
    username = db.Column(db.String(80), unique=True, nullable=False, index=True)
    display_name = db.Column(db.String(120), nullable=False)
    password_hash = db.Column(db.String(255), nullable=False)
    role = db.Column(db.String(20), nullable=False, default='manager')
    active = db.Column(db.Boolean, nullable=False, default=True)
    dashboard_period = db.Column(db.String(20), nullable=False, default='always')
    created_at = db.Column(db.DateTime, nullable=False, default=datetime.utcnow)
    updated_at = db.Column(
        db.DateTime, nullable=False, default=datetime.utcnow, onupdate=datetime.utcnow
    )
    last_login_at = db.Column(db.DateTime)

    audit_logs = db.relationship('AuditLog', backref='actor', lazy=True)

    @property
    def is_active(self):
        return self.active

    @property
    def is_admin(self):
        return self.role == 'admin'

    def set_password(self, password):
        self.password_hash = generate_password_hash(password)

    def check_password(self, password):
        return check_password_hash(self.password_hash, password)


class AuditLog(db.Model):
    """Journal append-only des actions effectuees dans l'application."""
    __tablename__ = 'audit_log'

    id = db.Column(db.Integer, primary_key=True)
    actor_id = db.Column(db.Integer, db.ForeignKey('user.id'), nullable=True, index=True)
    actor_username = db.Column(db.String(80), nullable=False)
    action = db.Column(db.String(50), nullable=False, index=True)
    target_type = db.Column(db.String(50), nullable=False, index=True)
    target_id = db.Column(db.String(80))
    details = db.Column(db.Text, nullable=False, default='{}')
    created_at = db.Column(db.DateTime, nullable=False, index=True, default=datetime.utcnow)

    @property
    def details_data(self):
        try:
            return json.loads(self.details or '{}')
        except (TypeError, ValueError):
            return {}


class Product(db.Model):
    """
    Représente un produit en stock.
    
    Attributs:
        id: Identifiant unique du produit
        name: Nom du produit
        sku: Code de suivi de stock (unique)
        description: Description du produit
        current_stock: Quantité actuelle en stock
        threshold: Seuil d'alerte critique
        created_at: Date de création
        updated_at: Date de dernière modification
    """
    __tablename__ = 'product'
    
    id = db.Column(db.Integer, primary_key=True)
    name = db.Column(db.String(100), nullable=False, unique=True)
    sku = db.Column(db.String(50), unique=True, nullable=False)
    description = db.Column(db.Text, default='')
    current_stock = db.Column(db.Integer, default=0, nullable=False)
    threshold = db.Column(db.Integer, default=10, nullable=False)
    created_at = db.Column(db.DateTime, default=datetime.utcnow)
    updated_at = db.Column(db.DateTime, default=datetime.utcnow, onupdate=datetime.utcnow)
    
    # Relations
    movements = db.relationship('Movement', backref='product', lazy=True, cascade='all, delete-orphan')
    alerts = db.relationship('Alert', backref='product', lazy=True, cascade='all, delete-orphan')
    
    def __repr__(self):
        return f'<Product {self.name} - Stock: {self.current_stock}>'
    
    @property
    def is_critical(self):
        """Retourne True si le stock est critique (0)."""
        return self.current_stock <= 0
    
    @property
    def is_low(self):
        """Retourne True si le stock est bas (sous le seuil)."""
        return self.current_stock < self.threshold and self.current_stock > 0
    
    @property
    def status(self):
        """Retourne le statut du produit."""
        if self.is_critical:
            return 'Rupture de stock'
        elif self.is_low:
            return 'Stock faible'
        return 'Stock suffisant'


class Movement(db.Model):
    """
    Enregistre chaque entrée ou sortie de stock.
    Fournit un historique complet des mouvements.
    
    Attributs:
        id: Identifiant unique du mouvement
        product_id: Référence au produit
        quantity_change: Quantité (positive pour entrée, négative pour sortie)
        movement_type: Type de mouvement ('IN' ou 'OUT')
        unit_price: Prix unitaire applique au mouvement
        source_direction/source_agent: Origine du mouvement
        destination_direction/destination_agent: Destination du mouvement
        reason: Motif du mouvement
        recorded_by: Utilisateur qui a enregistré le mouvement
        timestamp: Date et heure du mouvement
    """
    __tablename__ = 'movement'
    
    id = db.Column(db.Integer, primary_key=True)
    product_id = db.Column(db.Integer, db.ForeignKey('product.id'), nullable=False)
    quantity_change = db.Column(db.Integer, nullable=False)
    movement_type = db.Column(db.String(10), nullable=False)
    unit_price = db.Column(db.Numeric(12, 2), nullable=False, default=0)
    source_direction = db.Column(db.String(120), nullable=False, default='')
    source_agent = db.Column(db.String(120), nullable=False, default='')
    destination_direction = db.Column(db.String(120), nullable=False, default='')
    destination_agent = db.Column(db.String(120), nullable=False, default='')
    reason = db.Column(db.String(255), default='')
    recorded_by = db.Column(db.String(80), default='System')
    timestamp = db.Column(db.DateTime, index=True, default=datetime.utcnow)
    
    def __repr__(self):
        return f'<Movement {self.movement_type} de {abs(self.quantity_change)} unités>'
    
    @property
    def is_entry(self):
        """Retourne True si c'est une entrée de stock."""
        return self.movement_type == 'IN'
    
    @property
    def is_exit(self):
        """Retourne True si c'est une sortie de stock."""
        return self.movement_type == 'OUT'

    @property
    def total_value(self):
        """Valeur totale du mouvement (quantite absolue x prix unitaire)."""
        return abs(self.quantity_change) * self.unit_price


class Alert(db.Model):
    """
    Enregistre les alertes de stock critique.
    Crée des alertes quand le stock passe sous le seuil.
    
    Attributs:
        id: Identifiant unique de l'alerte
        product_id: Référence au produit
        alert_type: Type d'alerte ('CRITICAL' ou 'WARNING')
        message: Message descriptif
        is_resolved: True si l'alerte a été traitée
        created_at: Date de création
    """
    __tablename__ = 'alert'
    
    id = db.Column(db.Integer, primary_key=True)
    product_id = db.Column(db.Integer, db.ForeignKey('product.id'), nullable=False)
    alert_type = db.Column(db.String(50), nullable=False)
    message = db.Column(db.Text, nullable=False)
    is_resolved = db.Column(db.Boolean, default=False)
    created_at = db.Column(db.DateTime, index=True, default=datetime.utcnow)
    
    def __repr__(self):
        return f'<Alert {self.alert_type} - {self.product.name}>'


def check_and_create_alert(product: Product) -> bool:
    """
    Vérifie le stock et crée une alerte si nécessaire.
    
    Args:
        product: L'objet Product à vérifier
        
    Returns:
        True si une alerte a été créée, False sinon
    """
    # Vérifier s'il existe une alerte non résolue pour ce produit
    existing_unresolved = Alert.query.filter(
        Alert.product_id == product.id,
        Alert.is_resolved == False
    ).first()
    
    if product.current_stock <= 0:
        alert_type = 'CRITICAL'
        message = f"🚨 RUPTURE DE STOCK: '{product.name}' - Quantité actuelle: {product.current_stock}"
    elif product.current_stock < product.threshold:
        alert_type = 'WARNING'
        message = f"⚠️ STOCK FAIBLE: '{product.name}' ({product.current_stock} unités) - Seuil: {product.threshold}"
    else:
        # Résoudre les alertes existantes si le stock est redevenu suffisant
        if existing_unresolved:
            existing_unresolved.is_resolved = True
        return False
    
    # Créer une nouvelle alerte si aucune n'existe
    if not existing_unresolved:
        new_alert = Alert(
            product_id=product.id,
            alert_type=alert_type,
            message=message,
            is_resolved=False
        )
        db.session.add(new_alert)
        return True
    
    return False
