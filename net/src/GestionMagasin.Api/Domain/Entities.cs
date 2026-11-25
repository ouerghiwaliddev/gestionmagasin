namespace GestionMagasin.Api.Domain;

public static class Roles
{
    public const string Admin = "admin";
    public const string Manager = "manager";
    /// <summary>Consultation : lecture et exports uniquement, aucune modification du stock ni des données.</summary>
    public const string Viewer = "viewer";

    public static bool IsValid(string? role) => role is Admin or Manager or Viewer;
}

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Manager;
    public bool Active { get; set; } = true;
    public string DashboardPeriod { get; set; } = "always";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    public bool IsAdmin => Role == Roles.Admin;
}

/// <summary>Journal append-only des actions effectuées dans l'application.</summary>
public class AuditLog
{
    public int Id { get; set; }
    public int? ActorId { get; set; }
    public string ActorUsername { get; set; } = "";
    public string Action { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string? TargetId { get; set; }
    public string Details { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Liste de référence (fournisseur, agent, service) : jamais supprimée, seulement désactivée.</summary>
public interface IReference
{
    int Id { get; set; }
    string Name { get; set; }
    bool Active { get; set; }
}

public class Supplier : IReference
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
}

public class Agent : IReference
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
}

public class Service : IReference
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Sku { get; set; } = "";
    public string Description { get; set; } = "";
    public int CurrentStock { get; set; }
    public int Threshold { get; set; } = 10;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Movement> Movements { get; set; } = [];
    public List<Alert> Alerts { get; set; } = [];

    public bool IsCritical => CurrentStock <= 0;
    public bool IsLow => CurrentStock > 0 && CurrentStock < Threshold;
    public string Status => IsCritical ? "Rupture de stock" : IsLow ? "Stock faible" : "Stock suffisant";
}

public static class MovementTypes
{
    public const string In = "IN";
    public const string Out = "OUT";

    public static bool IsValid(string? type) => type is In or Out;
}

/// <summary>Entrée ou sortie de stock. Prix unitaire HT, taxe en pourcentage.</summary>
public class Movement
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Positive pour une entrée, négative pour une sortie.</summary>
    public int QuantityChange { get; set; }
    public string MovementType { get; set; } = MovementTypes.In;
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public int? AgentId { get; set; }
    public Agent? Agent { get; set; }
    public int? ServiceId { get; set; }
    public Service? Service { get; set; }

    /// <summary>Ancienne saisie texte « source -> destination » (mouvements importés de l'ancienne version).</summary>
    public string LegacyText { get; set; } = "";

    public string Reason { get; set; } = "";
    public string RecordedBy { get; set; } = "System";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public bool IsEntry => MovementType == MovementTypes.In;
    public decimal TotalHt => Math.Abs(QuantityChange) * UnitPrice;
    public decimal TaxAmount => Money.TaxOf(TotalHt, TaxRate);
    public decimal TotalTtc => TotalHt + TaxAmount;
}

public class Alert
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string AlertType { get; set; } = "";
    public string Message { get; set; } = "";
    public bool IsResolved { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class Money
{
    /// <summary>Montant de taxe arrondi au centime (demi supérieur), comme dans l'ancienne application.</summary>
    public static decimal TaxOf(decimal totalHt, decimal ratePercent) =>
        Math.Round(totalHt * ratePercent / 100m, 2, MidpointRounding.AwayFromZero);
}
