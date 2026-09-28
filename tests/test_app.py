import os
import tempfile
import unittest
from datetime import datetime, timedelta
from decimal import Decimal


TEST_DB = os.path.join(tempfile.gettempdir(), 'gestionmagasin_auth_tests.db')
os.environ['DATABASE_URL'] = 'sqlite:///' + TEST_DB.replace('\\', '/')
os.environ['SECRET_KEY'] = 'unit-test-secret'
os.environ['ADMIN_USERNAME'] = 'bootstrap-admin'
os.environ['ADMIN_PASSWORD'] = 'Bootstrap123!'

from app import app, bootstrap_admin, db  # noqa: E402
from models import Alert, AuditLog, Movement, Product, User  # noqa: E402


class ApplicationTestCase(unittest.TestCase):
    def setUp(self):
        app.config.update(TESTING=True, WTF_CSRF_ENABLED=False)
        with app.app_context():
            db.drop_all()
            db.create_all()
            admin = User(username='admin', display_name='Admin', role='admin', active=True)
            admin.set_password('AdminPass123!')
            manager = User(username='manager', display_name='Gestionnaire', role='manager', active=True)
            manager.set_password('ManagerPass123!')
            db.session.add_all([admin, manager])
            db.session.commit()
        self.client = app.test_client()

    def login(self, username='admin', password='AdminPass123!'):
        return self.client.post('/login', data={'username': username, 'password': password})

    def test_first_admin_bootstrap(self):
        with app.app_context():
            db.session.query(AuditLog).delete()
            db.session.query(User).delete()
            db.session.commit()
            os.environ['ADMIN_USERNAME'] = 'first-admin'
            os.environ['ADMIN_PASSWORD'] = 'FirstAdmin123!'
            bootstrap_admin()
            user = User.query.one()
            self.assertEqual(user.username, 'first-admin')
            self.assertTrue(user.is_admin)
            self.assertTrue(user.check_password('FirstAdmin123!'))
            os.environ['ADMIN_USERNAME'] = 'bootstrap-admin'
            os.environ['ADMIN_PASSWORD'] = 'Bootstrap123!'

    def test_authentication_and_api_protection(self):
        self.assertEqual(self.client.get('/').status_code, 302)
        self.assertEqual(self.client.get('/api/dashboard/stats').status_code, 401)
        self.assertEqual(self.client.post('/login', data={'username': 'admin', 'password': 'bad'}).status_code, 401)
        self.assertEqual(self.login().status_code, 302)
        self.assertEqual(self.client.get('/').status_code, 200)
        self.assertEqual(self.client.get('/admin/users').status_code, 200)
        self.assertEqual(self.client.get('/admin/audit').status_code, 200)
        with app.app_context():
            self.assertEqual(AuditLog.query.filter_by(action='LOGIN').count(), 1)

    def test_manager_can_operate_stock_but_not_administer(self):
        self.login('manager', 'ManagerPass123!')
        self.assertEqual(self.client.get('/admin/users').status_code, 403)
        response = self.client.post('/product/add', data={
            'name': 'Clavier', 'sku': 'CL-1', 'description': '',
            'initial_stock': '5', 'threshold': '2',
        })
        self.assertEqual(response.status_code, 302)
        with app.app_context():
            product = Product.query.filter_by(sku='CL-1').one()
            product_id = product.id
        response = self.client.post(f'/product/{product_id}/movement', data={
            'quantity': '3', 'movement_type': 'IN', 'unit_price': '10.50',
            'source_direction': 'A', 'source_agent': 'Agent A',
            'destination_direction': 'B', 'destination_agent': 'Agent B',
            'reason': 'Réception',
        })
        self.assertEqual(response.status_code, 302)
        with app.app_context():
            movement = Movement.query.one()
            self.assertEqual(movement.recorded_by, 'manager')
            actions = {entry.action for entry in AuditLog.query.all()}
            self.assertIn('PRODUCT_CREATE', actions)
            self.assertIn('STOCK_MOVEMENT', actions)

    def test_rolling_period_is_persisted_and_filters_activity_only(self):
        with app.app_context():
            product = Product(name='Produit', sku='P-1', current_stock=0, threshold=5)
            db.session.add(product)
            db.session.flush()
            now = datetime.utcnow()
            for age, movement_type, quantity, price in [
                (10, 'IN', 2, '5'), (60, 'OUT', -1, '4'),
                (200, 'IN', 3, '2'), (400, 'OUT', -4, '1'),
            ]:
                db.session.add(Movement(
                    product_id=product.id, movement_type=movement_type,
                    quantity_change=quantity, unit_price=Decimal(price),
                    timestamp=now - timedelta(days=age), recorded_by='manager',
                ))
            db.session.add(Alert(product_id=product.id, alert_type='CRITICAL', message='Rupture'))
            db.session.commit()
        self.login('manager', 'ManagerPass123!')
        self.client.post('/dashboard/period', data={'period': 'monthly'})
        payload = self.client.get('/api/dashboard/stats').get_json()
        self.assertEqual(payload['period'], 'monthly')
        self.assertEqual(payload['recent_movements'], 1)
        self.assertEqual(payload['entry_quantity'], 2)
        self.assertEqual(payload['critical_products'], 1)
        self.assertEqual(payload['active_alerts'], 1)
        with app.app_context():
            self.assertEqual(User.query.filter_by(username='manager').one().dashboard_period, 'monthly')

    def test_user_safeguards_and_deactivation(self):
        self.login()
        self.client.post('/admin/users/create', data={
            'username': 'second', 'display_name': 'Second',
            'role': 'manager', 'password': 'SecondPass123!',
        })
        with app.app_context():
            admin_id = User.query.filter_by(username='admin').one().id
            second_id = User.query.filter_by(username='second').one().id
        self.client.post(f'/admin/users/{admin_id}/deactivate')
        self.client.post(f'/admin/users/{second_id}/deactivate')
        with app.app_context():
            self.assertTrue(User.query.get(admin_id).active)
            self.assertFalse(User.query.get(second_id).active)
            self.assertGreaterEqual(AuditLog.query.filter_by(action='USER_DEACTIVATE').count(), 1)

    def test_product_deletion_keeps_audit_and_invalid_operation_does_not_log(self):
        self.login()
        self.client.post('/product/add', data={
            'name': 'Écran', 'sku': 'E-1', 'initial_stock': '1', 'threshold': '0',
        })
        with app.app_context():
            product_id = Product.query.filter_by(sku='E-1').one().id
            count_before = AuditLog.query.count()
        self.client.post('/product/add', data={
            'name': 'Invalide', 'sku': 'BAD', 'initial_stock': 'abc', 'threshold': '0',
        })
        with app.app_context():
            self.assertEqual(AuditLog.query.count(), count_before)
        self.client.post(f'/product/{product_id}/delete')
        with app.app_context():
            self.assertIsNone(db.session.get(Product, product_id))
            self.assertEqual(AuditLog.query.filter_by(action='PRODUCT_DELETE', target_id=str(product_id)).count(), 1)

    def test_csrf_rejects_unprotected_post(self):
        app.config['WTF_CSRF_ENABLED'] = True
        try:
            response = self.client.post('/login', data={'username': 'admin', 'password': 'AdminPass123!'})
            self.assertEqual(response.status_code, 400)
        finally:
            app.config['WTF_CSRF_ENABLED'] = False


if __name__ == '__main__':
    unittest.main()
