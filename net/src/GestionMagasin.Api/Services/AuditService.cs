using System.Text.Encodings.Web;
using System.Text.Json;
using GestionMagasin.Api.Data;
using GestionMagasin.Api.Domain;

namespace GestionMagasin.Api.Services;

/// <summary>Ajoute des entrées au journal d'audit dans la transaction (SaveChanges) en cours.</summary>
public class AuditService(AppDbContext db, CurrentUser current)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<AuditLog> AddAsync(
        string action, string targetType, object? targetId = null,
        object? before = null, object? after = null, User? actor = null)
    {
        actor ??= await current.GetAsync();
        var entry = new AuditLog
        {
            ActorId = actor?.Id,
            ActorUsername = actor?.Username ?? "system",
            Action = action,
            TargetType = targetType,
            TargetId = targetId?.ToString(),
            Details = JsonSerializer.Serialize(new { before, after }, JsonOptions),
            CreatedAt = DateTime.UtcNow,
        };
        db.AuditLogs.Add(entry);
        return entry;
    }

    public static object ProductSnapshot(Product p) => new
    {
        p.Name, p.Sku, p.Description, current_stock = p.CurrentStock, p.Threshold,
    };

    public static object UserSnapshot(User u) => new
    {
        u.Username, display_name = u.DisplayName, u.Role, u.Active,
    };
}
