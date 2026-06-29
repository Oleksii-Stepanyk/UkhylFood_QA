using KSE.DistributedSystems.OrderService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KSE.DistributedSystems.OrderService.DataAccess.Configurations;

public class PaymentMethodConfiguration : IEntityTypeConfiguration<PaymentMethod>
{
    public void Configure(EntityTypeBuilder<PaymentMethod> builder)
    {
        builder.HasKey(pm => pm.Id);

        builder.HasIndex(pm => pm.CustomerId);

        builder.Property(pm => pm.MethodType)
            .HasConversion<string>();
        builder.Property(pm => pm.Details)
            .HasMaxLength(500);
    }
}