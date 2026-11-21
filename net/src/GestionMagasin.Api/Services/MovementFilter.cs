using System.Globalization;
using GestionMagasin.Api.Domain;

namespace GestionMagasin.Api.Services;

/// <summary>Filtres de recherche des mouvements (historique et exports).</summary>
public class MovementFilter
{
    public int? ProductId { get; set; }
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public string? MovementType { get; set; }
    public int? AgentId { get; set; }
    public int? SupplierId { get; set; }
    public int? ServiceId { get; set; }

    /// <summary>Applique les filtres ; retourne un message d'erreur si la plage de dates est invalide.</summary>
    public (IQueryable<Movement> Query, string? Error) Apply(IQueryable<Movement> query)
    {
        if (ProductId is int productId && productId > 0) query = query.Where(m => m.ProductId == productId);

        DateTime? from = null, to = null;
        if (!TryParseDate(DateFrom, out from) || !TryParseDate(DateTo, out to))
            return (query.Where(_ => false), "La plage de dates est invalide.");
        if (from is not null && to is not null && from > to)
            return (query.Where(_ => false), "La date de début doit précéder la date de fin.");

        if (from is DateTime f) query = query.Where(m => m.Timestamp >= f);
        if (to is DateTime t)
        {
            var endExclusive = t.AddDays(1);
            query = query.Where(m => m.Timestamp < endExclusive);
        }

        if (MovementType is MovementTypes.In or MovementTypes.Out) query = query.Where(m => m.MovementType == MovementType);
        if (AgentId is int agentId && agentId > 0) query = query.Where(m => m.AgentId == agentId);
        if (SupplierId is int supplierId && supplierId > 0) query = query.Where(m => m.SupplierId == supplierId);
        if (ServiceId is int serviceId && serviceId > 0) query = query.Where(m => m.ServiceId == serviceId);
        return (query, null);
    }

    private static bool TryParseDate(string? value, out DateTime? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return false;
        date = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return true;
    }
}
