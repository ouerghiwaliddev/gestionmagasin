using GestionMagasin.Api.Domain;

namespace GestionMagasin.Api.Dtos;

public record ApiError(string Message);

public record PageDto<T>(IReadOnlyList<T> Items, int Page, int Pages, int Total, int PerPage);

// ---------- Réponses ----------

public record UserDto(
    int Id, string Username, string DisplayName, string Role, bool Active,
    string DashboardPeriod, DateTime CreatedAt, DateTime? LastLoginAt);

public record ProductDto(
    int Id, string Name, string Sku, string Description, int CurrentStock, int Threshold,
    string Status, bool IsCritical, bool IsLow, DateTime CreatedAt, DateTime UpdatedAt);

public record MovementDto(
    int Id, int ProductId, string ProductName, string ProductSku,
    string MovementType, int QuantityChange,
    decimal UnitPrice, decimal TaxRate, decimal TotalHt, decimal TaxAmount, decimal TotalTtc,
    int? SupplierId, string? SupplierName,
    int? AgentId, string? AgentName,
    int? ServiceId, string? ServiceName,
    string LegacyText, string Reason, string RecordedBy, DateTime Timestamp);

public record AlertDto(
    int Id, int ProductId, string ProductName, string ProductSku,
    string AlertType, string Message, bool IsResolved, DateTime CreatedAt);

public record ReferenceDto(int Id, string Name, bool Active);

public record AuditLogDto(
    int Id, int? ActorId, string ActorUsername, string Action, string TargetType,
    string? TargetId, System.Text.Json.JsonElement Details, DateTime CreatedAt);

public record DashboardStatsDto(
    int TotalProducts, int CriticalProducts, int LowStockProducts,
    int RecentMovements,
    int EntryCount, int EntryQuantity, decimal EntryValue,
    int ExitCount, int ExitQuantity, decimal ExitValue,
    int ActiveAlerts, string Period, string PeriodLabel,
    DateTime? PeriodStart, DateTime PeriodEnd);

public record InventoryItemDto(
    int ProductId, string Sku, string Name, int Stock,
    int EntryQuantity, int CumulQuantity, decimal CumulTotalTtc,
    decimal? Cmp, decimal? StockValue);

public record InventoryDto(
    int Year, IReadOnlyList<int> Years, IReadOnlyList<InventoryItemDto> Items,
    decimal TotalValue, int MissingCmp);

// ---------- Requêtes ----------

public record LoginRequest(string? Username, string? Password);
public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword, string? PasswordConfirmation);
public record PeriodRequest(string? Period);

public record ProductCreateRequest(string? Name, string? Sku, string? Description, int InitialStock, int Threshold = 10);
public record ProductUpdateRequest(string? Name, string? Description, int Threshold);

public record MovementRequest(
    int Quantity, string? MovementType, decimal? UnitPrice, decimal? TaxRate, string? Reason,
    string? RecipientType, int? SupplierId, int? AgentId, int? ServiceId);

public record NameRequest(string? Name);

public record UserCreateRequest(string? Username, string? DisplayName, string? Role, string? Password);
public record UserUpdateRequest(string? DisplayName, string? Role);
public record PasswordRequest(string? Password);

// ---------- Mappings ----------

public static class Mappings
{
    public static UserDto ToDto(this User u) =>
        new(u.Id, u.Username, u.DisplayName, u.Role, u.Active, u.DashboardPeriod, u.CreatedAt, u.LastLoginAt);

    public static ProductDto ToDto(this Product p) =>
        new(p.Id, p.Name, p.Sku, p.Description, p.CurrentStock, p.Threshold,
            p.Status, p.IsCritical, p.IsLow, p.CreatedAt, p.UpdatedAt);

    public static MovementDto ToDto(this Movement m) =>
        new(m.Id, m.ProductId, m.Product.Name, m.Product.Sku, m.MovementType, m.QuantityChange,
            m.UnitPrice, m.TaxRate, m.TotalHt, m.TaxAmount, m.TotalTtc,
            m.SupplierId, m.Supplier?.Name, m.AgentId, m.Agent?.Name, m.ServiceId, m.Service?.Name,
            m.LegacyText, m.Reason, m.RecordedBy, m.Timestamp);

    public static AlertDto ToDto(this Alert a) =>
        new(a.Id, a.ProductId, a.Product.Name, a.Product.Sku, a.AlertType, a.Message, a.IsResolved, a.CreatedAt);

    public static ReferenceDto ToDto(this IReference r) => new(r.Id, r.Name, r.Active);
}
