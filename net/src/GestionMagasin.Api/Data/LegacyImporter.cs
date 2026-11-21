using System.Data;
using System.Globalization;
using GestionMagasin.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GestionMagasin.Api.Data;

/// <summary>
/// Importe produits, mouvements, alertes et listes de référence depuis la base SQLite de l'ancienne
/// application Flask (inventory.db). Les comptes et le journal d'audit ne sont pas repris : les mots de
/// passe de l'ancienne version (hachage Werkzeug) ne sont pas vérifiables ici.
/// </summary>
public static class LegacyImporter
{
    public static async Task<string> ImportAsync(AppDbContext db, string legacyPath)
    {
        if (!File.Exists(legacyPath)) throw new FileNotFoundException($"Base introuvable : {legacyPath}");
        if (await db.Products.AnyAsync() || await db.Movements.AnyAsync())
            throw new InvalidOperationException("La base cible contient déjà des produits ou des mouvements : import refusé.");

        var cs = new SqliteConnectionStringBuilder { DataSource = legacyPath, Mode = SqliteOpenMode.ReadOnly }.ToString();
        await using var source = new SqliteConnection(cs);
        await source.OpenAsync();

        await using var transaction = await db.Database.BeginTransactionAsync();

        var suppliers = await ReadAsync(source, "supplier", r => new Supplier { Id = Int(r, "id"), Name = Str(r, "name"), Active = Bool(r, "active", true) });
        var agents = await ReadAsync(source, "agent", r => new Agent { Id = Int(r, "id"), Name = Str(r, "name"), Active = Bool(r, "active", true) });
        var services = await ReadAsync(source, "service", r => new Service { Id = Int(r, "id"), Name = Str(r, "name"), Active = Bool(r, "active", true) });
        var products = await ReadAsync(source, "product", r => new Product
        {
            Id = Int(r, "id"), Name = Str(r, "name"), Sku = Str(r, "sku"), Description = Str(r, "description"),
            CurrentStock = Int(r, "current_stock"), Threshold = Int(r, "threshold", 10),
            CreatedAt = Date(r, "created_at") ?? DateTime.UtcNow, UpdatedAt = Date(r, "updated_at") ?? DateTime.UtcNow,
        });
        var movements = await ReadAsync(source, "movement", r =>
        {
            var legacy = LegacyText(Str(r, "source_direction"), Str(r, "source_agent"),
                Str(r, "destination_direction"), Str(r, "destination_agent"));
            var supplierId = NullableInt(r, "supplier_id");
            var agentId = NullableInt(r, "agent_id");
            var serviceId = NullableInt(r, "service_id");
            return new Movement
            {
                Id = Int(r, "id"), ProductId = Int(r, "product_id"),
                QuantityChange = Int(r, "quantity_change"), MovementType = Str(r, "movement_type"),
                UnitPrice = Dec(r, "unit_price"), TaxRate = Dec(r, "tax_rate"),
                SupplierId = supplierId, AgentId = agentId, ServiceId = serviceId,
                LegacyText = supplierId is null && agentId is null && serviceId is null ? legacy : "",
                Reason = Str(r, "reason"), RecordedBy = Str(r, "recorded_by", "System"),
                Timestamp = Date(r, "timestamp") ?? DateTime.UtcNow,
            };
        });
        var alerts = await ReadAsync(source, "alert", r => new Alert
        {
            Id = Int(r, "id"), ProductId = Int(r, "product_id"), AlertType = Str(r, "alert_type"),
            Message = Str(r, "message"), IsResolved = Bool(r, "is_resolved", false),
            CreatedAt = Date(r, "created_at") ?? DateTime.UtcNow,
        });

        db.Suppliers.AddRange(suppliers);
        db.Agents.AddRange(agents);
        db.Services.AddRange(services);
        db.Products.AddRange(products);
        await db.SaveChangesAsync();
        db.Movements.AddRange(movements);
        db.Alerts.AddRange(alerts);
        await db.SaveChangesAsync();

        // Les identifiants ont été repris explicitement : réaligner les séquences PostgreSQL, sinon
        // la prochaine insertion réutiliserait un Id existant.
        foreach (var table in new[] { "Suppliers", "Agents", "Services", "Products", "Movements", "Alerts" })
        {
#pragma warning disable EF1002 // noms de tables constants, aucune donnée utilisateur
            await db.Database.ExecuteSqlRawAsync(
                $"SELECT setval(pg_get_serial_sequence('\"{table}\"', 'Id'), COALESCE((SELECT MAX(\"Id\") FROM \"{table}\"), 1), (SELECT COUNT(*) > 0 FROM \"{table}\"))");
#pragma warning restore EF1002
        }

        await transaction.CommitAsync();

        return $"Import terminé : {products.Count} produits, {movements.Count} mouvements, {alerts.Count} alertes, " +
               $"{suppliers.Count} fournisseurs, {agents.Count} agents, {services.Count} services.";
    }

    private static async Task<List<T>> ReadAsync<T>(SqliteConnection connection, string table, Func<IDataRecord, T> map)
    {
        await using (var exists = connection.CreateCommand())
        {
            exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name";
            exists.Parameters.AddWithValue("$name", table);
            if (await exists.ExecuteScalarAsync() is null) return [];
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM \"{table}\"";
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync()) rows.Add(map(new ColumnReader(reader)));
        return rows;
    }

    private static string LegacyText(string sourceDirection, string sourceAgent, string destinationDirection, string destinationAgent)
    {
        var source = string.Join(" / ", new[] { sourceDirection, sourceAgent }.Where(v => v.Length > 0));
        var destination = string.Join(" / ", new[] { destinationDirection, destinationAgent }.Where(v => v.Length > 0));
        return source.Length > 0 && destination.Length > 0 ? $"{source} -> {destination}" : source + destination;
    }

    // ---- lecture tolérante aux colonnes absentes ----

    private sealed class ColumnReader(IDataRecord record) : IDataRecord
    {
        public int FieldCount => record.FieldCount;
        public object this[int i] => record[i];
        public object this[string name] => record[name];
        public bool GetBoolean(int i) => record.GetBoolean(i);
        public byte GetByte(int i) => record.GetByte(i);
        public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length) => record.GetBytes(i, fieldOffset, buffer, bufferOffset, length);
        public char GetChar(int i) => record.GetChar(i);
        public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => record.GetChars(i, fieldoffset, buffer, bufferoffset, length);
        public IDataReader GetData(int i) => record.GetData(i);
        public string GetDataTypeName(int i) => record.GetDataTypeName(i);
        public DateTime GetDateTime(int i) => record.GetDateTime(i);
        public decimal GetDecimal(int i) => record.GetDecimal(i);
        public double GetDouble(int i) => record.GetDouble(i);
        public Type GetFieldType(int i) => record.GetFieldType(i);
        public float GetFloat(int i) => record.GetFloat(i);
        public Guid GetGuid(int i) => record.GetGuid(i);
        public short GetInt16(int i) => record.GetInt16(i);
        public int GetInt32(int i) => record.GetInt32(i);
        public long GetInt64(int i) => record.GetInt64(i);
        public string GetName(int i) => record.GetName(i);
        public int GetOrdinal(string name) => record.GetOrdinal(name);
        public string GetString(int i) => record.GetString(i);
        public object GetValue(int i) => record.GetValue(i);
        public int GetValues(object[] values) => record.GetValues(values);
        public bool IsDBNull(int i) => record.IsDBNull(i);
    }

    private static object? Raw(IDataRecord r, string column)
    {
        for (var i = 0; i < r.FieldCount; i++)
            if (string.Equals(r.GetName(i), column, StringComparison.OrdinalIgnoreCase))
                return r.IsDBNull(i) ? null : r.GetValue(i);
        return null;
    }

    private static string Str(IDataRecord r, string column, string fallback = "") =>
        Convert.ToString(Raw(r, column), CultureInfo.InvariantCulture) ?? fallback;

    private static int Int(IDataRecord r, string column, int fallback = 0) =>
        Raw(r, column) is { } v ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : fallback;

    private static int? NullableInt(IDataRecord r, string column) =>
        Raw(r, column) is { } v ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : null;

    private static bool Bool(IDataRecord r, string column, bool fallback) =>
        Raw(r, column) is { } v ? Convert.ToInt32(v, CultureInfo.InvariantCulture) != 0 : fallback;

    private static decimal Dec(IDataRecord r, string column) =>
        Raw(r, column) switch
        {
            null => 0m,
            string s => decimal.Parse(s, CultureInfo.InvariantCulture),
            var v => Convert.ToDecimal(v, CultureInfo.InvariantCulture),
        };

    private static DateTime? Date(IDataRecord r, string column) =>
        Raw(r, column) is string s && DateTime.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
            : null;
}
