using GestionMagasin.Api.Data;
using GestionMagasin.Api.Dtos;
using GestionMagasin.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionMagasin.Api.Controllers;

[Route("api/dashboard")]
public class DashboardController(AppDbContext db, CurrentUser current, AuditService audit, StockService stock) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get()
    {
        var user = await current.RequireAsync();
        var products = (await db.Products.AsNoTracking().ToListAsync()).OrderBy(p => p.Name, Text.French).ToList();
        var alerts = await db.Alerts.AsNoTracking().Include(a => a.Product)
            .Where(a => !a.IsResolved).OrderByDescending(a => a.CreatedAt).ToListAsync();
        return Ok(new
        {
            stats = await stock.GetDashboardStatsAsync(user.DashboardPeriod),
            products = products.Select(p => p.ToDto()),
            alerts = alerts.Select(a => a.ToDto()),
        });
    }

    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> Stats()
    {
        var user = await current.RequireAsync();
        return Ok(await stock.GetDashboardStatsAsync(user.DashboardPeriod));
    }

    [HttpPut("period")]
    public async Task<ActionResult> SetPeriod(PeriodRequest request)
    {
        if (request.Period is null || !Periods.All.ContainsKey(request.Period)) return Fail("Période invalide.");
        var user = await current.RequireAsync();
        var previous = user.DashboardPeriod;
        user.DashboardPeriod = request.Period;
        await audit.AddAsync("DASHBOARD_PERIOD_CHANGE", "user", user.Id,
            before: new { dashboard_period = previous }, after: new { dashboard_period = request.Period });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("/api/export/dashboard/{format}")]
    public async Task<IActionResult> Export(string format)
    {
        var user = await current.RequireAsync();
        var products = (await db.Products.AsNoTracking().ToListAsync()).OrderBy(p => p.Name, Text.French).ToList();
        var alerts = await db.Alerts.AsNoTracking().Include(a => a.Product)
            .Where(a => !a.IsResolved).OrderByDescending(a => a.CreatedAt).ToListAsync();
        var s = await stock.GetDashboardStatsAsync(user.DashboardPeriod);

        var indicators = new List<object?[]>
        {
            new object?[] { "Produits", s.TotalProducts },
            new object?[] { "Ruptures de stock", s.CriticalProducts },
            new object?[] { "Stocks faibles", s.LowStockProducts },
            new object?[] { $"Mouvements - {s.PeriodLabel}", s.RecentMovements },
            new object?[] { "Nombre d'entrées", s.EntryCount },
            new object?[] { "Quantités entrées", s.EntryQuantity },
            new object?[] { "Valeur des entrées HT (TND)", s.EntryValue },
            new object?[] { "Nombre de sorties", s.ExitCount },
            new object?[] { "Quantités sorties", s.ExitQuantity },
            new object?[] { "Valeur des sorties HT (TND)", s.ExitValue },
            new object?[] { "Alertes actives", s.ActiveAlerts },
        };
        return SendExport(
        [
            new ExportSection("Indicateurs", ["Indicateur", "Valeur"], indicators),
            new ExportSection("Produits", ExportRows.ProductHeaders, ExportRows.Products(products)),
            new ExportSection("Alertes actives", ExportRows.AlertHeaders, ExportRows.Alerts(alerts)),
        ], "tableau_de_bord", format);
    }
}
