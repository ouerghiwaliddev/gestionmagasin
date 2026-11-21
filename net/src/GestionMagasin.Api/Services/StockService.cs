using System.Globalization;
using GestionMagasin.Api.Data;
using GestionMagasin.Api.Domain;
using GestionMagasin.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace GestionMagasin.Api.Services;

public static class Periods
{
    public static readonly IReadOnlyDictionary<string, (string Label, int? Days)> All =
        new Dictionary<string, (string, int?)>
        {
            ["always"] = ("Tous", null),
            ["yearly"] = ("Annuel", 365),
            ["quarterly"] = ("Trimestriel", 90),
            ["monthly"] = ("Mensuel", 30),
        };
}

public static class Tax
{
    /// <summary>Taux proposés à la saisie (en %) ; le serveur accepte toute valeur de 0 à 100.</summary>
    public static readonly int[] Rates = [0, 7, 13, 19];
    public const int DefaultRate = 19;
}

public static class Text
{
    public static readonly StringComparer French =
        StringComparer.Create(CultureInfo.GetCultureInfo("fr-FR"), ignoreCase: true);
}

public class StockService(AppDbContext db)
{
    private static readonly decimal Zero = 0m;

    /// <summary>
    /// Vérifie le stock et crée, met à jour ou résout l'alerte du produit.
    /// Doit être appelé avant SaveChanges, le produit étant déjà suivi.
    /// </summary>
    public async Task<bool> CheckAndCreateAlertAsync(Product product)
    {
        var existing = await db.Alerts.FirstOrDefaultAsync(a => a.ProductId == product.Id && !a.IsResolved);

        string type, message;
        if (product.CurrentStock <= 0)
        {
            type = "CRITICAL";
            message = $"🚨 RUPTURE DE STOCK: '{product.Name}' - Quantité actuelle: {product.CurrentStock}";
        }
        else if (product.CurrentStock < product.Threshold)
        {
            type = "WARNING";
            message = $"⚠️ STOCK FAIBLE: '{product.Name}' ({product.CurrentStock} unités) - Seuil: {product.Threshold}";
        }
        else
        {
            if (existing is not null) existing.IsResolved = true;
            return false;
        }

        if (existing is not null)
        {
            existing.AlertType = type;
            existing.Message = message;
            return false;
        }

        db.Alerts.Add(new Alert { Product = product, AlertType = type, Message = message, CreatedAt = DateTime.UtcNow });
        return true;
    }

    public async Task<DashboardStatsDto> GetDashboardStatsAsync(string? period)
    {
        var key = period is not null && Periods.All.ContainsKey(period) ? period : "always";
        var (label, days) = Periods.All[key];
        var end = DateTime.UtcNow;
        DateTime? start = days is int d ? end.AddDays(-d) : null;

        var products = await db.Products.AsNoTracking().ToListAsync();
        var query = db.Movements.AsNoTracking().Where(m => m.Timestamp <= end);
        if (start is DateTime s) query = query.Where(m => m.Timestamp >= s);
        var movements = await query.ToListAsync();

        var entries = movements.Where(m => m.IsEntry).ToList();
        var exits = movements.Where(m => !m.IsEntry).ToList();

        return new DashboardStatsDto(
            products.Count,
            products.Count(p => p.IsCritical),
            products.Count(p => p.IsLow),
            movements.Count,
            entries.Count,
            entries.Sum(m => m.QuantityChange),
            entries.Sum(m => m.TotalHt),
            exits.Count,
            exits.Sum(m => Math.Abs(m.QuantityChange)),
            exits.Sum(m => m.TotalHt),
            await db.Alerts.CountAsync(a => !a.IsResolved),
            key, label, start, end);
    }

    // ---------- Inventaire et coût moyen pondéré (CMP) ----------

    /// <summary>Exercices (années civiles) couverts par les mouvements, jusqu'à l'année en cours (décroissant).</summary>
    public async Task<List<int>> InventoryYearsAsync()
    {
        var currentYear = DateTime.UtcNow.Year;
        var first = await db.Movements.OrderBy(m => m.Timestamp).Select(m => (DateTime?)m.Timestamp).FirstOrDefaultAsync();
        var firstYear = first is DateTime f ? Math.Min(f.Year, currentYear) : currentYear;
        return Enumerable.Range(firstYear, currentYear - firstYear + 1).Reverse().ToList();
    }

    /// <summary>Cumul des entrées par produit et par exercice : (quantité, total TTC).</summary>
    public async Task<Dictionary<(int ProductId, int Year), (int Quantity, decimal TotalTtc)>> EntryTotalsByYearAsync()
    {
        var rows = await db.Movements.AsNoTracking()
            .Where(m => m.MovementType == MovementTypes.In)
            .Select(m => new { m.ProductId, m.Timestamp, m.QuantityChange, m.UnitPrice, m.TaxRate })
            .ToListAsync();

        var totals = new Dictionary<(int, int), (int, decimal)>();
        foreach (var r in rows)
        {
            var totalHt = Math.Abs(r.QuantityChange) * r.UnitPrice;
            var ttc = totalHt + Money.TaxOf(totalHt, r.TaxRate);
            var key = (r.ProductId, r.Timestamp.Year);
            totals.TryGetValue(key, out var acc);
            totals[key] = (acc.Item1 + r.QuantityChange, acc.Item2 + ttc);
        }
        return totals;
    }

    /// <summary>CMP TTC = total TTC des entrées ÷ quantités entrées (3 décimales) ; null sans entrée.</summary>
    public static decimal? WeightedAverageCost(int quantity, decimal totalTtc) =>
        quantity == 0 ? null : Math.Round(totalTtc / quantity, 3, MidpointRounding.AwayFromZero);

    /// <summary>Entrées cumulées de l'origine jusqu'à la fin de l'exercice inclus.</summary>
    public static (int Quantity, decimal TotalTtc) CumulativeEntries(
        Dictionary<(int ProductId, int Year), (int Quantity, decimal TotalTtc)> totals, int productId, int year)
    {
        var quantity = 0;
        var total = 0m;
        foreach (var ((pid, y), value) in totals)
        {
            if (pid != productId || y > year) continue;
            quantity += value.Quantity;
            total += value.TotalTtc;
        }
        return (quantity, total);
    }

    /// <summary>
    /// Inventaire d'un exercice : stock à la clôture, CMP TTC pondéré sur les entrées cumulées
    /// jusqu'à cet exercice, et valeur du stock (stock × CMP).
    /// </summary>
    public async Task<List<InventoryItemDto>> BuildInventoryAsync(int year)
    {
        var yearEnd = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var totals = await EntryTotalsByYearAsync();

        // Le stock initial n'est pas un mouvement : on remonte depuis le stock actuel.
        var laterChanges = await db.Movements.AsNoTracking()
            .Where(m => m.Timestamp >= yearEnd)
            .GroupBy(m => m.ProductId)
            .Select(g => new { ProductId = g.Key, Change = g.Sum(m => m.QuantityChange) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Change);

        var products = await db.Products.AsNoTracking().ToListAsync();
        var items = new List<InventoryItemDto>();
        foreach (var product in products.OrderBy(p => p.Name, Text.French))
        {
            if (product.CreatedAt >= yearEnd) continue;
            var stock = product.CurrentStock - laterChanges.GetValueOrDefault(product.Id);
            totals.TryGetValue((product.Id, year), out var ofYear);
            var (cumulQuantity, cumulTtc) = CumulativeEntries(totals, product.Id, year);
            var cmp = WeightedAverageCost(cumulQuantity, cumulTtc);
            items.Add(new InventoryItemDto(
                product.Id, product.Sku, product.Name, stock,
                ofYear.Quantity, cumulQuantity, Math.Round(cumulTtc, 2, MidpointRounding.AwayFromZero),
                cmp,
                cmp is decimal c ? Math.Round(stock * c, 2, MidpointRounding.AwayFromZero) : null));
        }
        return items;
    }

    public async Task<InventoryDto> GetInventoryAsync(int? requestedYear)
    {
        var years = await InventoryYearsAsync();
        var year = requestedYear is int y && years.Contains(y) ? y : years[0];
        var items = await BuildInventoryAsync(year);
        return new InventoryDto(
            year, years, items,
            items.Sum(i => i.StockValue ?? Zero),
            items.Count(i => i.Cmp is null && i.Stock != 0));
    }

    /// <summary>CMP TTC cumulé de chaque produit pour chaque exercice, à partir de sa première entrée.</summary>
    public async Task<List<object?[]>> CmpHistoryRowsAsync()
    {
        var totals = await EntryTotalsByYearAsync();
        var firstYear = new Dictionary<int, int>();
        foreach (var (productId, year) in totals.Keys)
            firstYear[productId] = firstYear.TryGetValue(productId, out var f) ? Math.Min(f, year) : year;

        var products = (await db.Products.AsNoTracking().ToListAsync())
            .Where(p => firstYear.ContainsKey(p.Id))
            .OrderBy(p => p.Name, Text.French)
            .ToList();

        var rows = new List<object?[]>();
        foreach (var year in await InventoryYearsAsync())
        {
            foreach (var product in products)
            {
                if (year < firstYear[product.Id]) continue;
                totals.TryGetValue((product.Id, year), out var ofYear);
                var (quantity, ttc) = CumulativeEntries(totals, product.Id, year);
                rows.Add([year, product.Sku, product.Name, ofYear.Quantity, quantity,
                    Math.Round(ttc, 2, MidpointRounding.AwayFromZero), WeightedAverageCost(quantity, ttc)]);
            }
        }
        return rows;
    }
}
