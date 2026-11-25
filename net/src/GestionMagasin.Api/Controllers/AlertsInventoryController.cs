using GestionMagasin.Api.Data;
using GestionMagasin.Api.Dtos;
using GestionMagasin.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionMagasin.Api.Controllers;

[Route("api/alerts")]
public class AlertsController(AppDbContext db, AuditService audit) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult> List()
    {
        var active = await db.Alerts.AsNoTracking().Include(a => a.Product)
            .Where(a => !a.IsResolved).OrderByDescending(a => a.CreatedAt).ToListAsync();
        var resolved = await db.Alerts.AsNoTracking().Include(a => a.Product)
            .Where(a => a.IsResolved).OrderByDescending(a => a.CreatedAt).Take(10).ToListAsync();
        return Ok(new { active = active.Select(a => a.ToDto()), resolved = resolved.Select(a => a.ToDto()) });
    }

    [Authorize(Policy = "Write")]
    [HttpPost("{id:int}/resolve")]
    public async Task<ActionResult> Resolve(int id)
    {
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id);
        if (alert is null) return Missing("Alerte introuvable.");

        var before = new { is_resolved = alert.IsResolved, message = alert.Message };
        alert.IsResolved = true;
        await audit.AddAsync("ALERT_RESOLVE", "alert", alert.Id, before, new { is_resolved = true, message = alert.Message });
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("/api/export/alerts/{format}")]
    public async Task<IActionResult> Export(string format)
    {
        var alerts = await db.Alerts.AsNoTracking().Include(a => a.Product).OrderByDescending(a => a.CreatedAt).ToListAsync();
        return SendExport([new ExportSection("Alertes", ExportRows.AlertHeaders, ExportRows.Alerts(alerts))], "alertes", format);
    }
}

[Route("api/inventory")]
public class InventoryController(StockService stock) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<InventoryDto>> Get([FromQuery] int? exercice) =>
        Ok(await stock.GetInventoryAsync(exercice));

    [HttpGet("/api/export/inventory/{format}")]
    public async Task<IActionResult> Export(string format, [FromQuery] int? exercice)
    {
        var inventory = await stock.GetInventoryAsync(exercice);
        var rows = inventory.Items.Select(i => new object?[]
        {
            i.Sku, i.Name, i.Stock, i.EntryQuantity, i.CumulQuantity, i.CumulTotalTtc, i.Cmp, i.StockValue,
        }).ToList();
        rows.Add(["", "TOTAL", inventory.Items.Sum(i => i.Stock), null, null, null, null, inventory.TotalValue]);

        string?[] inventoryFormats = [null, null, null, null, null, "0.00", "0.000", "0.00"];
        string?[] cmpFormats = [null, null, null, null, null, "0.00", "0.000"];
        return SendExport(
        [
            new ExportSection(
                $"Inventaire {inventory.Year}",
                ["SKU", "Produit", "Stock à la clôture", "Quantités entrées (exercice)", "Quantités entrées (cumul)",
                 "Total TTC entrées (cumul, TND)", "CMP TTC cumulé (TND)", "Valeur du stock (TND)"],
                rows, inventoryFormats),
            new ExportSection(
                "CMP cumulé par exercice",
                ["Exercice", "SKU", "Produit", "Quantités entrées (exercice)", "Quantités entrées (cumul)",
                 "Total TTC entrées (cumul, TND)", "CMP TTC cumulé (TND)"],
                await stock.CmpHistoryRowsAsync(), cmpFormats),
        ], $"inventaire_{inventory.Year}", format);
    }
}
