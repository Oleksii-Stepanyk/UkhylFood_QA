using KSE.DistributedSystems.OrderService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KSE.DistributedSystems.OrderService.DataAccess.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.HasKey(i => i.OrderId);

        builder.HasIndex(i => i.CustomerId);
        builder.HasIndex(i => i.RestaurantId);

        builder.Property(i => i.Currency)
            .HasMaxLength(15);

        builder.Property(i => i.PaymentStatus)
            .HasConversion<string>();

        builder.HasOne(i => i.PaymentMethod)
            .WithMany(pm => pm.Invoices);
    }
}