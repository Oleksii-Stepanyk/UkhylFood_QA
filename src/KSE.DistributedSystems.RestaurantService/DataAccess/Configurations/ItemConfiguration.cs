using KSE.DistributedSystems.RestaurantService.DataAccess.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KSE.DistributedSystems.RestaurantService.DataAccess.Configurations;

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.HasKey(i => i.Id);

        builder.HasIndex(i => i.RestaurantId);

        builder.Property(i => i.Name)
            .HasMaxLength(50);
        builder.Property(i => i.Description)
            .HasMaxLength(200);
        builder.Property(i => i.Category)
            .HasConversion<string>();
        
        builder.HasOne(i => i.Restaurant)
            .WithMany(r => r.MenuItems)
            .HasForeignKey(i => i.RestaurantId);
    }
}