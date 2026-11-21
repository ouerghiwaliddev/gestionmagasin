using GestionMagasin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace GestionMagasin.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Movement> Movements => Set<Movement>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Service> Services => Set<Service>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Les dates sont stockées en UTC ; relues avec Kind=Utc pour être sérialisées avec « Z ».
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        builder.Properties<decimal>().HavePrecision(12, 2);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasPostgresExtension("citext"); // identifiant insensible à la casse (remplace NOCASE de SQLite)

        model.Entity<User>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            e.Property(u => u.Username).HasMaxLength(80).HasColumnType("citext");
            e.Property(u => u.DisplayName).HasMaxLength(120);
            e.Property(u => u.Role).HasMaxLength(20);
            e.Property(u => u.DashboardPeriod).HasMaxLength(20);
        });

        model.Entity<AuditLog>(e =>
        {
            e.HasIndex(a => a.ActorId);
            e.HasIndex(a => a.Action);
            e.HasIndex(a => a.TargetType);
            e.HasIndex(a => a.CreatedAt);
            e.Property(a => a.ActorUsername).HasMaxLength(80);
            e.Property(a => a.Action).HasMaxLength(50);
            e.Property(a => a.TargetType).HasMaxLength(50);
            e.Property(a => a.TargetId).HasMaxLength(80);
        });

        model.Entity<Supplier>(e => { e.HasIndex(x => x.Name).IsUnique(); e.Property(x => x.Name).HasMaxLength(120); });
        model.Entity<Agent>(e => { e.HasIndex(x => x.Name).IsUnique(); e.Property(x => x.Name).HasMaxLength(120); });
        model.Entity<Service>(e => { e.HasIndex(x => x.Name).IsUnique(); e.Property(x => x.Name).HasMaxLength(120); });

        model.Entity<Product>(e =>
        {
            e.HasIndex(p => p.Name).IsUnique();
            e.HasIndex(p => p.Sku).IsUnique();
            e.Property(p => p.Name).HasMaxLength(100);
            e.Property(p => p.Sku).HasMaxLength(50);
            e.HasMany(p => p.Movements).WithOne(m => m.Product).HasForeignKey(m => m.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Alerts).WithOne(a => a.Product).HasForeignKey(a => a.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<Movement>(e =>
        {
            e.HasIndex(m => m.Timestamp);
            e.HasIndex(m => m.SupplierId);
            e.HasIndex(m => m.AgentId);
            e.HasIndex(m => m.ServiceId);
            e.Property(m => m.MovementType).HasMaxLength(10);
            e.Property(m => m.TaxRate).HasPrecision(5, 2);
            e.Property(m => m.Reason).HasMaxLength(255);
            e.Property(m => m.RecordedBy).HasMaxLength(80);
            e.Property(m => m.LegacyText).HasMaxLength(500);
            e.HasOne(m => m.Supplier).WithMany().HasForeignKey(m => m.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Agent).WithMany().HasForeignKey(m => m.AgentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Service).WithMany().HasForeignKey(m => m.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(m => m.IsEntry);
            e.Ignore(m => m.TotalHt);
            e.Ignore(m => m.TaxAmount);
            e.Ignore(m => m.TotalTtc);
        });

        model.Entity<Alert>(e =>
        {
            e.HasIndex(a => a.CreatedAt);
            e.Property(a => a.AlertType).HasMaxLength(50);
        });

        model.Entity<Product>().Ignore(p => p.IsCritical).Ignore(p => p.IsLow).Ignore(p => p.Status);
        model.Entity<User>().Ignore(u => u.IsAdmin);
    }
}

// Npgsql (timestamptz) refuse tout DateTime dont le Kind n'est pas Utc : on normalise à l'écriture.
public class UtcDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public class NullableUtcDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
    v => v.HasValue ? (v.Value.Kind == DateTimeKind.Local ? v.Value.ToUniversalTime() : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)) : v,
    v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
