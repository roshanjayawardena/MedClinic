using Billing.Domain;
using Core;
using Microsoft.EntityFrameworkCore;
using Persistence;

namespace Billing.Persistence;

public sealed class BillingDbContext(
    DbContextOptions<BillingDbContext> options,
    ITenantContext tenantContext,
    TimeProvider timeProvider)
    : BaseDbContext<BillingDbContext>(options, tenantContext, timeProvider)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("billing");

        modelBuilder.Entity<Invoice>(e =>
        {
            e.ToTable("invoices");

            e.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            e.Property(x => x.TotalAmount)
                .HasPrecision(18, 2)
                .IsRequired();

            e.Property(x => x.PaymentMethod).HasMaxLength(50);
            e.Property(x => x.VoidReason).HasMaxLength(500);

            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => new { x.TenantId, x.PatientId });
            e.HasIndex(x => new { x.TenantId, x.EncounterId }).IsUnique();

            e.OwnsMany(x => x.LineItems, li =>
            {
                li.ToTable("invoice_line_items");
                li.WithOwner().HasForeignKey("InvoiceId");
                li.Property<int>("Id").ValueGeneratedOnAdd();
                li.HasKey("Id");
                li.Property(p => p.Description).HasMaxLength(500).IsRequired();
                li.Property(p => p.ProcedureCode).HasMaxLength(20);
                li.Property(p => p.UnitPrice).HasPrecision(18, 2).IsRequired();
                li.Property(p => p.Quantity).IsRequired();
                li.Ignore(p => p.LineTotal);
            });

            e.Navigation(x => x.LineItems)
                .HasField("_lineItems")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        // Always call base LAST — applies global tenant + soft-delete filter to Invoice.
        base.OnModelCreating(modelBuilder);
    }
}
