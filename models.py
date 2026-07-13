"""
Modèles SQLAlchemy pour l'application de gestion de stock.
Gère les produits, mouvements de stock et alertes.
"""

from flask_sqlalchemy import SQLAlchemy
from datetime import datetime

db = SQLAlchemy()


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
        reason: Motif du mouvement
        recorded_by: Utilisateur qui a enregistré le mouvement
        timestamp: Date et heure du mouvement
    """
    __tablename__ = 'movement'
    
    id = db.Column(db.Integer, primary_key=True)
    product_id = db.Column(db.Integer, db.ForeignKey('product.id'), nullable=False)
    quantity_change = db.Column(db.Integer, nullable=False)
    movement_type = db.Column(db.String(10), nullable=False)
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
