using GestionMagasin.Api.Data;
using GestionMagasin.Api.Domain;
using GestionMagasin.Api.Dtos;
using GestionMagasin.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionMagasin.Api.Controllers;

public class MovementsController(
    AppDbContext db, CurrentUser current, AuditService audit, StockService stock) : ApiControllerBase
{
    public const int PageSize = 20;

    private IQueryable<Movement> WithRelations() =>
        db.Movements.AsNoTracking()
            .Include(m => m.Product).Include(m => m.Supplier).Include(m => m.Agent).Include(m => m.Service);

    /// <summary>Listes actives (fournisseurs, agents, services) et taux de taxe pour la saisie d'un mouvement.</summary>
    [HttpGet("api/lookups")]
    public async Task<ActionResult> Lookups()
    {
        async Task<List<ReferenceDto>> Active<T>(DbSet<T> set) where T : class, IReference =>
            (await set.AsNoTracking().Where(x => x.Active).ToListAsync())
            .OrderBy(x => x.Name, Text.French).Select(x => x.ToDto()).ToList();

        return Ok(new
        {
            suppliers = await Active(db.Suppliers),
            agents = await Active(db.Agents),
            services = await Active(db.Services),
            taxRates = Tax.Rates,
            defaultTaxRate = Tax.DefaultRate,
        });
    }

    [HttpGet("api/movements")]
    public async Task<ActionResult<PageDto<MovementDto>>> List([FromQuery] MovementFilter filter, [FromQuery] int page = 1)
    {
        var (query, error) = filter.Apply(WithRelations());
        if (error is not null) return Fail(error);

        page = Math.Max(page, 1);
        var total = await query.CountAsync();
        var pages = Math.Max((int)Math.Ceiling(total / (double)PageSize), 1);
        page = Math.Min(page, pages);
        var items = await query.OrderByDescending(m => m.Timestamp).ThenByDescending(m => m.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();
        return Ok(new PageDto<MovementDto>(items.Select(m => m.ToDto()).ToList(), page, pages, total, PageSize));
    }

    [HttpPost("api/products/{productId:int}/movements")]
    public async Task<ActionResult> Create(int productId, MovementRequest request)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId);
        if (product is null) return Missing("Produit introuvable.");

        if (request.Quantity <= 0) return Fail("La quantité doit être positive.");
        if (!MovementTypes.IsValid(request.MovementType)) return Fail("Type de mouvement invalide.");
        var type = request.MovementType!;
        var isEntry = type == MovementTypes.In;

        if (isEntry && request.UnitPrice is null) return Fail("Le prix unitaire est obligatoire pour une entrée.");
        var unitPrice = request.UnitPrice ?? 0m;
        if (unitPrice < 0) return Fail("Le prix unitaire ne peut pas être négatif.");
        var taxRate = request.TaxRate ?? Tax.DefaultRate;
        if (taxRate < 0 || taxRate > 100) return Fail("Le taux de taxe doit être compris entre 0 et 100 %.");

        // Contrepartie : fournisseur pour une entrée, agent ou service pour une sortie.
        Supplier? supplier = null;
        Agent? agent = null;
        Service? service = null;
        if (isEntry)
        {
            supplier = request.SupplierId is int sid ? await db.Suppliers.FirstOrDefaultAsync(s => s.Id == sid && s.Active) : null;
            if (supplier is null) return Fail("Sélectionnez un fournisseur pour une entrée.");
        }
        else if (request.RecipientType == "agent")
        {
            agent = request.AgentId is int aid ? await db.Agents.FirstOrDefaultAsync(a => a.Id == aid && a.Active) : null;
            if (agent is null) return Fail("Sélectionnez un agent.");
        }
        else if (request.RecipientType == "service")
        {
            service = request.ServiceId is int vid ? await db.Services.FirstOrDefaultAsync(s => s.Id == vid && s.Active) : null;
            if (service is null) return Fail("Sélectionnez un service.");
        }
        else
        {
            return Fail("Indiquez si la sortie est destinée à un agent ou à un service.");
        }

        string? warning = null;
        if (!isEntry && product.CurrentStock - request.Quantity < 0)
            warning = $"Attention : la quantité demandée dépasse le stock disponible ({product.CurrentStock}).";

        var user = await current.RequireAsync();
        var change = isEntry ? request.Quantity : -request.Quantity;
        var reason = (request.Reason ?? "").Trim();
        var movement = new Movement
        {
            Product = product,
            QuantityChange = change,
            MovementType = type,
            UnitPrice = unitPrice,
            TaxRate = taxRate,
            Supplier = supplier, Agent = agent, Service = service,
            Reason = reason.Length > 0 ? reason : (isEntry ? "Entrée" : "Sortie"),
            RecordedBy = user.Username,
            Timestamp = DateTime.UtcNow,
        };
        product.CurrentStock += change;
        product.UpdatedAt = DateTime.UtcNow;
        db.Movements.Add(movement);

        await using var transaction = await db.Database.BeginTransactionAsync();
        await stock.CheckAndCreateAlertAsync(product);
        await db.SaveChangesAsync();
        await audit.AddAsync("STOCK_MOVEMENT", "movement", movement.Id, after: new
        {
            product_id = product.Id,
            product = product.Name,
            type,
            quantity = request.Quantity,
            unit_price_ht = unitPrice,
            tax_rate = taxRate,
            total_ttc = movement.TotalTtc,
            supplier = supplier?.Name,
            agent = agent?.Name,
            service = service?.Name,
            stock_after = product.CurrentStock,
            reason = movement.Reason,
        });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(new
        {
            movement = movement.ToDto(),
            product = product.ToDto(),
            warning,
        });
    }

    [HttpGet("api/export/movements/{format}")]
    public async Task<IActionResult> Export(string format, [FromQuery] MovementFilter filter)
    {
        var (query, error) = filter.Apply(WithRelations());
        if (error is not null) return Fail(error);
        var movements = await query.OrderByDescending(m => m.Timestamp).ToListAsync();
        return SendExport(
            [new ExportSection("Mouvements de stock", ExportRows.MovementHeaders, ExportRows.Movements(movements), ExportRows.MovementFormats)],
            "mouvements", format);
    }
}
