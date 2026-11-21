using GestionMagasin.Api.Data;
using GestionMagasin.Api.Domain;
using GestionMagasin.Api.Dtos;
using GestionMagasin.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestionMagasin.Api.Controllers;

/// <summary>Listes de référence : fournisseurs, agents, services. Les entrées sont désactivées, jamais supprimées.</summary>
[Route("api/referentials")]
public class ReferentialsController(AppDbContext db, AuditService audit) : ApiControllerBase
{
    private static readonly Dictionary<string, (string Singular, string Plural, string Icon)> Kinds = new()
    {
        ["supplier"] = ("Fournisseur", "Fournisseurs", "fa-truck"),
        ["agent"] = ("Agent", "Agents", "fa-user"),
        ["service"] = ("Service", "Services", "fa-building"),
    };

    private async Task<List<ReferenceDto>> AllAsync<T>(DbSet<T> set) where T : class, IReference =>
        (await set.AsNoTracking().ToListAsync()).OrderBy(x => x.Name, Text.French).Select(x => x.ToDto()).ToList();

    [HttpGet]
    public async Task<ActionResult> List()
    {
        var items = new Dictionary<string, List<ReferenceDto>>
        {
            ["supplier"] = await AllAsync(db.Suppliers),
            ["agent"] = await AllAsync(db.Agents),
            ["service"] = await AllAsync(db.Services),
        };
        return Ok(Kinds.Select(k => new
        {
            kind = k.Key, singular = k.Value.Singular, plural = k.Value.Plural, icon = k.Value.Icon,
            items = items[k.Key],
        }));
    }

    [HttpPost("{kind}")]
    public Task<ActionResult> Create(string kind, NameRequest request) => kind switch
    {
        "supplier" => CreateAsync(db.Suppliers, kind, request),
        "agent" => CreateAsync(db.Agents, kind, request),
        "service" => CreateAsync(db.Services, kind, request),
        _ => Task.FromResult<ActionResult>(Missing("Liste inconnue.")),
    };

    [HttpPut("{kind}/{id:int}")]
    public Task<ActionResult> Rename(string kind, int id, NameRequest request) => kind switch
    {
        "supplier" => RenameAsync(db.Suppliers, kind, id, request),
        "agent" => RenameAsync(db.Agents, kind, id, request),
        "service" => RenameAsync(db.Services, kind, id, request),
        _ => Task.FromResult<ActionResult>(Missing("Liste inconnue.")),
    };

    [HttpPost("{kind}/{id:int}/toggle")]
    public Task<ActionResult> Toggle(string kind, int id) => kind switch
    {
        "supplier" => ToggleAsync(db.Suppliers, kind, id),
        "agent" => ToggleAsync(db.Agents, kind, id),
        "service" => ToggleAsync(db.Services, kind, id),
        _ => Task.FromResult<ActionResult>(Missing("Liste inconnue.")),
    };

    private string? ValidateName(string kind, string? raw, out string name)
    {
        name = (raw ?? "").Trim();
        return name.Length == 0 || name.Length > 120
            ? $"Le nom du {Kinds[kind].Singular.ToLowerInvariant()} est obligatoire (120 caractères maximum)."
            : null;
    }

    private static async Task<bool> NameTakenAsync<T>(DbSet<T> set, string name, int? excludeId = null) where T : class, IReference =>
        (await set.AsNoTracking().Select(x => new { x.Id, x.Name }).ToListAsync())
            .Any(x => x.Id != excludeId && string.Equals(x.Name, name, StringComparison.CurrentCultureIgnoreCase));

    private async Task<ActionResult> CreateAsync<T>(DbSet<T> set, string kind, NameRequest request) where T : class, IReference, new()
    {
        if (ValidateName(kind, request.Name, out var name) is { } error) return Fail(error);
        if (await NameTakenAsync(set, name)) return Fail($"{Kinds[kind].Singular} « {name} » existe déjà.");

        var item = new T { Name = name, Active = true };
        set.Add(item);
        await db.SaveChangesAsync();
        await audit.AddAsync("REFERENCE_CREATE", kind, item.Id, after: new { name });
        await db.SaveChangesAsync();
        return Ok(item.ToDto());
    }

    private async Task<ActionResult> RenameAsync<T>(DbSet<T> set, string kind, int id, NameRequest request) where T : class, IReference
    {
        var item = await set.FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return Missing();
        if (ValidateName(kind, request.Name, out var name) is { } error) return Fail(error);
        if (await NameTakenAsync(set, name, id)) return Fail($"{Kinds[kind].Singular} « {name} » existe déjà.");

        var before = new { name = item.Name };
        item.Name = name;
        await audit.AddAsync("REFERENCE_UPDATE", kind, item.Id, before, new { name });
        await db.SaveChangesAsync();
        return Ok(item.ToDto());
    }

    private async Task<ActionResult> ToggleAsync<T>(DbSet<T> set, string kind, int id) where T : class, IReference
    {
        var item = await set.FirstOrDefaultAsync(x => x.Id == id);
        if (item is null) return Missing();
        var before = new { name = item.Name, active = item.Active };
        item.Active = !item.Active;
        await audit.AddAsync(item.Active ? "REFERENCE_ACTIVATE" : "REFERENCE_DEACTIVATE", kind, item.Id,
            before, new { name = item.Name, active = item.Active });
        await db.SaveChangesAsync();
        return Ok(item.ToDto());
    }
}
