using KSE.DistributedSystems.CourierService.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KSE.DistributedSystems.CourierService.DataAccess.Configurations;

public class CourierConfiguration : IEntityTypeConfiguration<Courier>
{
    public void Configure(EntityTypeBuilder<Courier> builder)
    {
        builder.ToTable("couriers");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(c => c.FirstName)
            .HasColumnName("first_name")
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(c => c.LastName)
            .HasColumnName("last_name")
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.VehicleType)
            .HasColumnName("vehicle_type")
            .IsRequired()
            .HasConversion<string>();

        builder.OwnsOne(c => c.CurrentLocation, location =>
        {
            location.Property(l => l.Latitude).HasColumnName("latitude").IsRequired();
            location.Property(l => l.Longitude).HasColumnName("longitude").IsRequired();
        });

        // builder.HasIndex("latitude", "longitude")
        //     .HasDatabaseName("IX_Courier_Location");

        builder.Property(c => c.IsAvailable)
            .HasColumnName("is_available")
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasIndex(c => c.IsAvailable)
            .HasDatabaseName("IX_Courier_IsAvailable");

        builder.Property(c => c.Rating)
            .HasColumnName("rating")
            .IsRequired();
    }
}