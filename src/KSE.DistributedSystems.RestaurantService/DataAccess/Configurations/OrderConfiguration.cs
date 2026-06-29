using KSE.DistributedSystems.RestaurantService.DataAccess.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KSE.DistributedSystems.RestaurantService.DataAccess.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);

        builder.HasIndex(o => o.CustomerId);
        builder.HasIndex(o => o.RestaurantId);
        builder.HasIndex(o => o.CourierId);

        builder.Property(o => o.Status)
            .HasConversion<string>();
        builder.Property(o => o.PaymentStatus)
            .HasConversion<string>();
        
        builder.HasMany(o => o.Items)
            .WithMany(oi => oi.Orders);
    }
}