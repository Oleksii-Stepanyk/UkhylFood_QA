using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.Text.Json;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;

namespace KSE.DistributedSystems.PaymentService.DataAccess.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(p => p.Id);
        
        builder.Property(p => p.Id)
            .ValueGeneratedOnAdd();
            
        builder.Property(p => p.OrderId)
            .IsRequired();
            
        builder.Property(p => p.CustomerId)
            .IsRequired();
            
        builder.Property(p => p.Amount)
            .HasPrecision(18, 2)
            .IsRequired();
            
        builder.Property(p => p.Currency)
            .HasMaxLength(3)
            .IsRequired();
            
        builder.Property(p => p.PaymentMethod)
            .HasConversion<string>()
            .IsRequired();
            
        builder.Property(p => p.Status)
            .HasConversion<string>()
            .IsRequired();
            
        builder.Property(p => p.ExternalPaymentId)
            .HasMaxLength(255);
            
        builder.Property(p => p.FailureReason)
            .HasMaxLength(1000);
            
        builder.Property(p => p.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .ValueGeneratedOnAdd()
            .IsRequired();
            
        // Complex type configuration for PaymentMetadata
        builder.Property(p => p.Metadata)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<PaymentMetadata>(v, (JsonSerializerOptions?)null) ?? new PaymentMetadata()
            )
            .HasColumnType("jsonb");
            
        // Index for performance
        builder.HasIndex(p => p.OrderId);
        builder.HasIndex(p => p.CustomerId);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.CreatedAt);
        
        // Relationships
        builder.HasMany(p => p.Events)
            .WithOne(e => e.Payment)
            .HasForeignKey(e => e.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        builder.HasKey(e => e.Id);
        
        builder.Property(e => e.Id)
            .ValueGeneratedOnAdd();
            
        builder.Property(e => e.PaymentId)
            .IsRequired();
            
        builder.Property(e => e.EventType)
            .HasConversion<string>()
            .IsRequired();
            
        builder.Property(e => e.EventData)
            .HasColumnType("text")
            .IsRequired();
            
        builder.Property(e => e.Timestamp)
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .ValueGeneratedOnAdd()
            .IsRequired();
            
        // Index for performance
        builder.HasIndex(e => e.PaymentId);
        builder.HasIndex(e => e.Timestamp);
    }
} 