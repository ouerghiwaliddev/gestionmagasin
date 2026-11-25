using GestionMagasin.Api.Data;
using GestionMagasin.Api.Domain;
using GestionMagasin.Api.Dtos;
using GestionMagasin.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionMagasin.Api.Controllers;

[Route("api/products")]
public class ProductsController(AppDbContext db, AuditService audit, StockService stock) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProductDto>>> List()
    {
        var products = await db.Products.AsNoTracking().ToListAsync();
        return Ok(products.OrderBy(p => p.Name, Text.French).Select(p => p.ToDto()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult> Get(int id)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return Missing("Produit introuvable.");

        var movements = await db.Movements.AsNoTracking()
            .Include(m => m.Product).Include(m => m.Supplier).Include(m => m.Agent).Include(m => m.Service)
            .Where(m => m.ProductId == id)
            .OrderByDescending(m => m.Timestamp).Take(20).ToListAsync();
        var alerts = await db.Alerts.AsNoTracking().Include(a => a.Product)
            .Where(a => a.ProductId == id).OrderByDescending(a => a.CreatedAt).ToListAsync();

        return Ok(new
        {
            product = product.ToDto(),
            movements = movements.Select(m => m.ToDto()),
            alerts = alerts.Select(a => a.ToDto()),
        });
    }

    [HttpGet("{id:int}/stock")]
    public async Task<ActionResult> Stock(int id)
    {
        var p = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        return p is null
            ? Missing("Produit introuvable.")
            : Ok(new { p.Id, p.Name, p.CurrentStock, p.Threshold, p.Status, p.IsCritical, p.IsLow });
    }

    [Authorize(Policy = "Write")]
    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(ProductCreateRequest request)
    {
        var name = (request.Name ?? "").Trim();
        var sku = (request.Sku ?? "").Trim();
        if (name.Length == 0 || sku.Length == 0) return Fail("Le nom et le SKU sont obligatoires.");
        if (name.Length > 100 || sku.Length > 50) return Fail("Le nom (100) ou le SKU (50) est trop long.");
        if (request.InitialStock < 0 || request.Threshold < 0) return Fail("Le stock initial et le seuil ne peuvent pas être négatifs.");
        if (await db.Products.AnyAsync(p => p.Name == name || p.Sku == sku))
            return Fail("Un produit avec ce nom ou SKU existe déjà.");

        var product = new Product
        {
            Name = name, Sku = sku, Description = (request.Description ?? "").Trim(),
            CurrentStock = request.InitialStock, Threshold = request.Threshold,
        };
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.Products.Add(product);
        await db.SaveChangesAsync();

        await stock.CheckAndCreateAlertAsync(product);
        await audit.AddAsync("PRODUCT_CREATE", "product", product.Id, after: AuditService.ProductSnapshot(product));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product.ToDto());
    }

    [Authorize(Policy = "Write")]
    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProductDto>> Update(int id, ProductUpdateRequest request)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return Missing("Produit introuvable.");

        var name = (request.Name ?? "").Trim();
        if (name.Length == 0) return Fail("Le nom du produit est obligatoire.");
        if (name.Length > 100) return Fail("Le nom du produit est trop long (100 caractères maximum).");
        if (request.Threshold < 0) return Fail("Le seuil ne peut pas être négatif.");
        if (await db.Products.AnyAsync(p => p.Name == name && p.Id != id))
            return Fail("Un produit avec ce nom existe déjà.");

        var before = AuditService.ProductSnapshot(product);
        product.Name = name;
        product.Description = (request.Description ?? "").Trim();
        product.Threshold = request.Threshold;
        product.UpdatedAt = DateTime.UtcNow;

        await stock.CheckAndCreateAlertAsync(product);
        await audit.AddAsync("PRODUCT_UPDATE", "product", product.Id, before, AuditService.ProductSnapshot(product));
        await db.SaveChangesAsync();
        return Ok(product.ToDto());
    }

    /// <summary>Supprime le produit ainsi que ses mouvements et alertes (cascade).</summary>
    [Authorize(Policy = "Write")]
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Delete(int id)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return Missing("Produit introuvable.");

        await audit.AddAsync("PRODUCT_DELETE", "product", product.Id, before: AuditService.ProductSnapshot(product));
        db.Products.Remove(product);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:int}/export/{format}")]
    public async Task<IActionResult> Export(int id, string format)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return Missing("Produit introuvable.");
        var movements = await db.Movements.AsNoTracking()
            .Include(m => m.Product).Include(m => m.Supplier).Include(m => m.Agent).Include(m => m.Service)
            .Where(m => m.ProductId == id).OrderByDescending(m => m.Timestamp).ToListAsync();
        var alerts = await db.Alerts.AsNoTracking().Include(a => a.Product)
            .Where(a => a.ProductId == id).OrderByDescending(a => a.CreatedAt).ToListAsync();

        return SendExport(
        [
            new ExportSection("Produit", ExportRows.ProductHeaders, ExportRows.Products([product])),
            new ExportSection("Mouvements", ExportRows.MovementHeaders, ExportRows.Movements(movements), ExportRows.MovementFormats),
            new ExportSection("Alertes", ExportRows.AlertHeaders, ExportRows.Alerts(alerts)),
        ], $"produit_{product.Sku}", format);
    }
}
