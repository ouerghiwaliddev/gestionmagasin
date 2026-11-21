using GestionMagasin.Api.Domain;

namespace GestionMagasin.Api.Services;

/// <summary>En-têtes et lignes communs aux rapports Excel / PDF.</summary>
public static class ExportRows
{
    public static readonly string[] ProductHeaders =
        ["SKU", "Produit", "Description", "Stock", "Seuil", "État", "Créé le", "Modifié le"];

    public static readonly string[] MovementHeaders =
    [
        "SKU", "Produit", "Type", "Quantité", "Prix unitaire HT (TND)", "Taux taxe (%)",
        "Total HT (TND)", "Taxe (TND)", "Total TTC (TND)",
        "Fournisseur", "Agent", "Service", "Ancienne saisie (source -> destination)",
        "Motif", "Enregistré par", "Date",
    ];

    public static readonly string?[] MovementFormats =
        [null, null, null, null, "0.00", "0.00", "0.00", "0.00", "0.00", null, null, null, null, null, null, null];

    public static readonly string[] AlertHeaders = ["SKU", "Produit", "Type", "Message", "État", "Date"];

    public static List<object?[]> Products(IEnumerable<Product> products) =>
        products.Select(p => new object?[]
        {
            p.Sku, p.Name, p.Description, p.CurrentStock, p.Threshold, p.Status, p.CreatedAt, p.UpdatedAt,
        }).ToList();

    public static List<object?[]> Movements(IEnumerable<Movement> movements) =>
        movements.Select(m => new object?[]
        {
            m.Product.Sku, m.Product.Name, m.IsEntry ? "Entrée" : "Sortie", m.QuantityChange,
            m.UnitPrice, m.TaxRate, m.TotalHt, m.TaxAmount, m.TotalTtc,
            m.Supplier?.Name ?? "", m.Agent?.Name ?? "", m.Service?.Name ?? "",
            m.Supplier is null && m.Agent is null && m.Service is null ? m.LegacyText : "",
            m.Reason, m.RecordedBy, m.Timestamp,
        }).ToList();

    public static List<object?[]> Alerts(IEnumerable<Alert> alerts) =>
        alerts.Select(a => new object?[]
        {
            a.Product.Sku, a.Product.Name, a.AlertType, a.Message, a.IsResolved ? "Résolue" : "Active", a.CreatedAt,
        }).ToList();
}
