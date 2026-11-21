using System.Net;
using System.Text;
using System.Text.Json;
using GestionMagasin.Api.Data;
using GestionMagasin.Api.Domain;
using GestionMagasin.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;

namespace GestionMagasin.Tests;

public class ApiTests : IDisposable
{
    private readonly AppFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static object Entry(int quantity, decimal price, int supplierId, int tax = 19) => new
    {
        quantity, movementType = "IN", unitPrice = price, taxRate = tax, supplierId, reason = "Achat",
    };

    private async Task<(ApiClient Client, int ProductId, int SupplierId, int AgentId, int ServiceId)> SeedAsync()
    {
        var client = await _factory.NewClient().LoginAsync();
        var supplier = await client.PostId("/api/referentials/supplier", new { name = "Fournisseur A" });
        var agent = await client.PostId("/api/referentials/agent", new { name = "Agent A" });
        var service = await client.PostId("/api/referentials/service", new { name = "Service A" });
        var product = await client.PostId("/api/products", new { name = "Papier", sku = "PA-1", initialStock = 0, threshold = 1 });
        return (client, product, supplier, agent, service);
    }

    // ---------------- Authentification ----------------

    [Fact]
    public async Task Authentication_is_required_and_csrf_is_enforced()
    {
        var client = _factory.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Get("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Get("/api/dashboard/stats")).StatusCode);

        // POST sans jeton CSRF : refusé
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostWithoutXsrf("/api/auth/login", new { username = "admin", password = AppFactory.AdminPassword })).StatusCode);

        await client.Get("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.Post("/api/auth/login", new { username = "admin", password = "bad" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.Post("/api/auth/login", new { username = "ADMIN", password = AppFactory.AdminPassword })).StatusCode);

        var me = await client.GetJson("/api/auth/me");
        Assert.Equal("admin", me.GetProperty("user").GetProperty("username").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.Get("/api/products")).StatusCode);

        // POST authentifié sans jeton : refusé
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostWithoutXsrf("/api/products", new { name = "X", sku = "X" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.Post("/api/auth/logout")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.Get("/api/products")).StatusCode);
        Assert.Equal(1, _factory.Db(db => db.AuditLogs.Count(a => a.Action == "LOGIN")));
    }

    [Fact]
    public async Task Manager_can_operate_stock_but_not_administer()
    {
        var admin = await _factory.NewClient().LoginAsync();
        Assert.True((await admin.Post("/api/admin/users",
            new { username = "manager", displayName = "Gestionnaire", role = "manager", password = "ManagerPass123!" })).IsSuccessStatusCode);

        var manager = await _factory.NewClient().LoginAsync("manager", "ManagerPass123!");
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.Get("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.Get("/api/admin/audit")).StatusCode);

        var supplier = await manager.PostId("/api/referentials/supplier", new { name = "Fournisseur A" });
        var product = await manager.PostId("/api/products", new { name = "Clavier", sku = "CL-1", initialStock = 5, threshold = 2 });
        var response = await manager.Post($"/api/products/{product}/movements", Entry(3, 10.50m, supplier));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var movement = (await manager.Json(response)).GetProperty("movement");
        Assert.Equal("manager", movement.GetProperty("recordedBy").GetString());
        Assert.Equal(31.50m, movement.GetProperty("totalHt").GetDecimal());
        Assert.Equal(5.99m, movement.GetProperty("taxAmount").GetDecimal());
        Assert.Equal(37.49m, movement.GetProperty("totalTtc").GetDecimal());

        var actions = _factory.Db(db => db.AuditLogs.Select(a => a.Action).ToList());
        Assert.Contains("PRODUCT_CREATE", actions);
        Assert.Contains("STOCK_MOVEMENT", actions);
    }

    [Fact]
    public async Task User_safeguards_and_deactivation()
    {
        var admin = await _factory.NewClient().LoginAsync();
        var adminId = _factory.Db(db => db.Users.Single(u => u.Username == "admin").Id);
        var secondId = await admin.PostId("/api/admin/users",
            new { username = "second", displayName = "Second", role = "manager", password = "SecondPass123!" });

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.Post($"/api/admin/users/{adminId}/deactivate")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.Put($"/api/admin/users/{adminId}", new { displayName = "Admin", role = "manager" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.Post("/api/admin/users", new { username = "SECOND", displayName = "x", role = "manager", password = "Whatever123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.Post("/api/admin/users", new { username = "short", displayName = "x", role = "manager", password = "abc" })).StatusCode);

        // Un compte désactivé ne peut plus ouvrir de session, ni utiliser une session existante.
        var second = await _factory.NewClient().LoginAsync("second", "SecondPass123!");
        Assert.Equal(HttpStatusCode.OK, (await admin.Post($"/api/admin/users/{secondId}/deactivate")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.Get("/api/products")).StatusCode);
        var fresh = _factory.NewClient();
        await fresh.Get("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await fresh.Post("/api/auth/login", new { username = "second", password = "SecondPass123!" })).StatusCode);
    }

    // ---------------- Produits et mouvements ----------------

    [Fact]
    public async Task Movement_validation_by_type()
    {
        var (c, product, supplier, agent, service) = await SeedAsync();
        var url = $"/api/products/{product}/movements";

        async Task Rejected(object body) => Assert.Equal(HttpStatusCode.BadRequest, (await c.Post(url, body)).StatusCode);

        await Rejected(new { quantity = 2, movementType = "IN", unitPrice = 5, taxRate = 19 });                            // sans fournisseur
        await Rejected(new { quantity = 1, movementType = "OUT", taxRate = 19 });                                          // sans agent/service
        await Rejected(new { quantity = 1, movementType = "OUT", recipientType = "agent", serviceId = service });          // agent attendu
        await Rejected(new { quantity = 2, movementType = "IN", unitPrice = 5, taxRate = 150, supplierId = supplier });     // taxe hors bornes
        await Rejected(new { quantity = 2, movementType = "IN", taxRate = 19, supplierId = supplier });                     // prix manquant
        await Rejected(new { quantity = 0, movementType = "IN", unitPrice = 5, supplierId = supplier });
        await Rejected(new { quantity = 2, movementType = "IN", unitPrice = -1, supplierId = supplier });
        Assert.Equal(0, _factory.Db(db => db.Movements.Count()));

        Assert.True((await c.Post(url, Entry(4, 5m, supplier))).IsSuccessStatusCode);
        Assert.True((await c.Post(url, new { quantity = 1, movementType = "OUT", recipientType = "agent", agentId = agent })).IsSuccessStatusCode);
        Assert.True((await c.Post(url, new { quantity = 1, movementType = "OUT", recipientType = "service", serviceId = service })).IsSuccessStatusCode);

        Assert.Equal(3, _factory.Db(db => db.Movements.Count()));
        Assert.Equal(1, _factory.Db(db => db.Movements.Count(m => m.AgentId != null)));
        Assert.Equal(1, _factory.Db(db => db.Movements.Count(m => m.ServiceId != null)));
        Assert.Equal(2, _factory.Db(db => db.Products.Single().CurrentStock));
    }

    [Fact]
    public async Task Alerts_follow_stock_level()
    {
        var (c, product, supplier, agent, _) = await SeedAsync();
        var url = $"/api/products/{product}/movements";
        // stock 0, seuil 1 : alerte critique dès la création
        var alerts = await c.GetJson("/api/alerts");
        Assert.Single(alerts.GetProperty("active").EnumerateArray());

        await c.Post(url, Entry(5, 2m, supplier));                      // stock 5 : alerte résolue
        Assert.Empty((await c.GetJson("/api/alerts")).GetProperty("active").EnumerateArray());

        var overdraw = await c.Post(url, new { quantity = 6, movementType = "OUT", recipientType = "agent", agentId = agent });
        Assert.Equal(HttpStatusCode.OK, overdraw.StatusCode);           // dépassement : autorisé avec avertissement
        Assert.NotNull((await c.Json(overdraw)).GetProperty("warning").GetString());
        var active = (await c.GetJson("/api/alerts")).GetProperty("active").EnumerateArray().ToList();
        Assert.Single(active);
        Assert.Equal("CRITICAL", active[0].GetProperty("alertType").GetString());
    }

    [Fact]
    public async Task Inactive_reference_is_refused_and_names_are_unique()
    {
        var (c, product, supplier, _, _) = await SeedAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Post("/api/referentials/supplier", new { name = "fournisseur a" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.Post("/api/referentials/unknown", new { name = "x" })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await c.Post($"/api/referentials/supplier/{supplier}/toggle")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await c.Post($"/api/products/{product}/movements", Entry(2, 5m, supplier))).StatusCode);

        var lookups = await c.GetJson("/api/lookups");
        Assert.Empty(lookups.GetProperty("suppliers").EnumerateArray());
    }

    [Fact]
    public async Task Duplicate_products_are_refused_and_delete_keeps_audit()
    {
        var (c, product, _, _, _) = await SeedAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Post("/api/products", new { name = "Papier", sku = "Z", initialStock = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Post("/api/products", new { name = "Z", sku = "PA-1", initialStock = 1 })).StatusCode);
        var before = _factory.Db(db => db.AuditLogs.Count());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Post("/api/products", new { name = "Bad", sku = "BAD", initialStock = "abc" })).StatusCode);
        Assert.Equal(before, _factory.Db(db => db.AuditLogs.Count()));

        Assert.Equal(HttpStatusCode.NoContent, (await c.Delete($"/api/products/{product}")).StatusCode);
        Assert.Equal(0, _factory.Db(db => db.Products.Count()));
        Assert.Equal(1, _factory.Db(db => db.AuditLogs.Count(a => a.Action == "PRODUCT_DELETE" && a.TargetId == product.ToString())));
    }

    [Fact]
    public async Task Movement_filters()
    {
        var (c, product, supplier, agent, service) = await SeedAsync();
        var other = await c.PostId("/api/referentials/supplier", new { name = "Fournisseur B" });
        var url = $"/api/products/{product}/movements";
        await c.Post(url, Entry(10, 2m, supplier));
        await c.Post(url, Entry(10, 2m, other));
        await c.Post(url, new { quantity = 1, movementType = "OUT", recipientType = "agent", agentId = agent });
        await c.Post(url, new { quantity = 1, movementType = "OUT", recipientType = "service", serviceId = service });

        async Task<int> Count(string query) =>
            (await c.GetJson($"/api/movements?{query}")).GetProperty("total").GetInt32();

        Assert.Equal(4, await Count(""));
        Assert.Equal(2, await Count("movementType=IN"));
        Assert.Equal(2, await Count("movementType=OUT"));
        Assert.Equal(1, await Count($"supplierId={supplier}"));
        Assert.Equal(1, await Count($"agentId={agent}"));
        Assert.Equal(1, await Count($"serviceId={service}"));
        Assert.Equal(0, await Count($"movementType=IN&agentId={agent}"));
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        Assert.Equal(4, await Count($"dateFrom={today}&dateTo={today}"));
        Assert.Equal(0, await Count($"dateTo={DateTime.UtcNow.AddDays(-2):yyyy-MM-dd}"));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Get("/api/movements?dateFrom=2026-02-01&dateTo=2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Get("/api/movements?dateFrom=nope")).StatusCode);
    }

    [Fact]
    public async Task Rolling_period_is_persisted_and_filters_activity_only()
    {
        var client = await _factory.NewClient().LoginAsync();
        _factory.Db(db =>
        {
            var product = new Product { Name = "Produit", Sku = "P-1", CurrentStock = 0, Threshold = 5 };
            db.Products.Add(product);
            db.SaveChanges();
            var now = DateTime.UtcNow;
            foreach (var (age, type, quantity, price) in new[] { (10, "IN", 2, 5m), (60, "OUT", -1, 4m), (200, "IN", 3, 2m), (400, "OUT", -4, 1m) })
                db.Movements.Add(new Movement
                {
                    ProductId = product.Id, MovementType = type, QuantityChange = quantity, UnitPrice = price,
                    Timestamp = now.AddDays(-age), RecordedBy = "admin",
                });
            db.Alerts.Add(new Alert { ProductId = product.Id, AlertType = "CRITICAL", Message = "Rupture" });
            db.SaveChanges();
            return 0;
        });

        Assert.Equal(HttpStatusCode.NoContent, (await client.Put("/api/dashboard/period", new { period = "monthly" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.Put("/api/dashboard/period", new { period = "weekly" })).StatusCode);
        var stats = await client.GetJson("/api/dashboard/stats");
        Assert.Equal("monthly", stats.GetProperty("period").GetString());
        Assert.Equal(1, stats.GetProperty("recentMovements").GetInt32());
        Assert.Equal(2, stats.GetProperty("entryQuantity").GetInt32());
        Assert.Equal(1, stats.GetProperty("criticalProducts").GetInt32());
        Assert.Equal(1, stats.GetProperty("activeAlerts").GetInt32());
    }

    // ---------------- Inventaire, CMP et exports ----------------

    private void SeedCmpData()
    {
        _factory.Db(db =>
        {
            var product = new Product
            {
                Name = "Stylo", Sku = "S-1", CurrentStock = 55, Threshold = 1,
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            };
            db.Products.Add(product);
            db.SaveChanges();
            foreach (var (ts, change, type, price, tax) in new[]
            {
                (new DateTime(2025, 3, 1), 10, "IN", 2.00m, 19m),
                (new DateTime(2025, 6, 1), 30, "IN", 4.00m, 19m),
                (new DateTime(2025, 9, 1), -5, "OUT", 0m, 19m),
                (new DateTime(2026, 2, 1), 20, "IN", 5.00m, 0m),
            })
                db.Movements.Add(new Movement
                {
                    ProductId = product.Id, MovementType = type, QuantityChange = change, UnitPrice = price, TaxRate = tax,
                    Timestamp = DateTime.SpecifyKind(ts, DateTimeKind.Utc), RecordedBy = "admin",
                });
            db.SaveChanges();
            return 0;
        });
    }

    [Fact]
    public async Task Inventory_weighted_average_cost_is_cumulative_per_exercise_and_ttc()
    {
        SeedCmpData();
        using var scope = _factory.Services.CreateScope();
        var stock = scope.ServiceProvider.GetRequiredService<StockService>();

        // 2025 : (10*2*1.19 + 30*4*1.19) / 40 = 166.60 / 40 = 4.165 ; stock fin 2025 = 55 - 20 = 35
        var item2025 = (await stock.BuildInventoryAsync(2025)).Single();
        Assert.Equal(4.165m, item2025.Cmp);
        Assert.Equal(35, item2025.Stock);
        Assert.Equal(145.78m, item2025.StockValue);   // 35 * 4.165 = 145.775 -> 145.78

        // 2026 cumule 2025 + 2026 : (166.60 + 20*5) / 60 = 4.4433 -> 4.443
        var item2026 = (await stock.BuildInventoryAsync(2026)).Single();
        Assert.Equal(4.443m, item2026.Cmp);
        Assert.Equal(60, item2026.CumulQuantity);
        Assert.Equal(20, item2026.EntryQuantity);
        Assert.Equal(55, item2026.Stock);

        var history = await stock.CmpHistoryRowsAsync();
        Assert.Equal([2026, 2025], history.Select(r => (int)r[0]!).ToArray());
    }

    [Fact]
    public async Task Product_without_entries_has_no_cmp()
    {
        var client = await _factory.NewClient().LoginAsync();
        await client.Post("/api/products", new { name = "Sans entrée", sku = "SE-1", initialStock = 3, threshold = 1 });
        var inventory = await client.GetJson("/api/inventory");
        var item = inventory.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, item.GetProperty("cmp").ValueKind);
        Assert.Equal(1, inventory.GetProperty("missingCmp").GetInt32());
    }

    [Fact]
    public async Task Exports_are_valid_excel_and_pdf_files()
    {
        SeedCmpData();
        var client = await _factory.NewClient().LoginAsync();
        var productId = _factory.Db(db => db.Products.Single().Id);

        foreach (var url in new[]
        {
            "/api/export/dashboard/{0}", "/api/export/movements/{0}?movementType=IN", "/api/export/alerts/{0}",
            "/api/export/inventory/{0}?exercice=2025", $"/api/products/{productId}/export/{{0}}",
        })
        {
            var excel = await client.Get(string.Format(url, "excel"));
            Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
            var xlsx = await excel.Content.ReadAsByteArrayAsync();
            Assert.Equal((byte)'P', xlsx[0]);                  // archive zip : « PK »
            Assert.Equal((byte)'K', xlsx[1]);

            var pdf = await client.Get(string.Format(url, "pdf"));
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            var bytes = await pdf.Content.ReadAsByteArrayAsync();
            Assert.StartsWith("%PDF-1.4", Encoding.Latin1.GetString(bytes, 0, 8));
            Assert.EndsWith("%%EOF", Encoding.Latin1.GetString(bytes, bytes.Length - 5, 5));
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.Get("/api/export/alerts/csv")).StatusCode);
    }

    // ---------------- Import de l'ancienne base ----------------

    [Fact]
    public async Task Legacy_database_can_be_imported()
    {
        var legacyPath = Path.Combine(Path.GetTempPath(), $"legacy-{Guid.NewGuid():N}.db");
        try
        {
            await using (var legacy = new SqliteConnection($"Data Source={legacyPath}"))
            {
                await legacy.OpenAsync();
                await using var command = legacy.CreateCommand();
                command.CommandText = """
                    CREATE TABLE product (id INTEGER PRIMARY KEY, name TEXT, sku TEXT, description TEXT, current_stock INTEGER,
                                          threshold INTEGER, created_at TEXT, updated_at TEXT);
                    CREATE TABLE movement (id INTEGER PRIMARY KEY, product_id INTEGER, quantity_change INTEGER, movement_type TEXT,
                                           unit_price NUMERIC, source_direction TEXT, source_agent TEXT, destination_direction TEXT,
                                           destination_agent TEXT, reason TEXT, recorded_by TEXT, timestamp TEXT);
                    CREATE TABLE alert (id INTEGER PRIMARY KEY, product_id INTEGER, alert_type TEXT, message TEXT, is_resolved BOOLEAN, created_at TEXT);
                    INSERT INTO product VALUES (1, 'Clavier', 'CL-1', '', 7, 2, '2025-03-01 10:00:00.123456', '2025-03-01 10:00:00');
                    INSERT INTO movement VALUES (1, 1, 7, 'IN', 12.5, 'Dir A', 'Agent A', 'Dir B', 'Agent B', 'Achat', 'admin', '2025-03-02 08:30:00.500000');
                    INSERT INTO alert VALUES (1, 1, 'WARNING', 'Stock faible', 1, '2025-03-03 09:00:00');
                    """;
                await command.ExecuteNonQueryAsync();
            }

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var message = await LegacyImporter.ImportAsync(db, legacyPath);
            Assert.Contains("1 produits", message);

            var movement = await db.Movements.AsNoTracking().SingleAsync();
            Assert.Equal(12.5m, movement.UnitPrice);
            Assert.Equal(0m, movement.TaxRate);
            Assert.Equal("Dir A / Agent A -> Dir B / Agent B", movement.LegacyText);
            Assert.Equal(DateTimeKind.Utc, movement.Timestamp.Kind);
            Assert.Equal(new DateTime(2025, 3, 2, 8, 30, 0, 500, DateTimeKind.Utc), movement.Timestamp);
            Assert.True((await db.Alerts.AsNoTracking().SingleAsync()).IsResolved);

            await Assert.ThrowsAsync<InvalidOperationException>(() => LegacyImporter.ImportAsync(db, legacyPath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(legacyPath); } catch (IOException) { }
        }
    }
}
