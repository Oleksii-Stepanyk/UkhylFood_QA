using Microsoft.EntityFrameworkCore;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;
using KSE.DistributedSystems.PaymentService.DataAccess.Configurations;
using MassTransit;

namespace KSE.DistributedSystems.PaymentService.DataAccess;

public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options)
    {
    }

    public DbSet<Payment> Payments { get; set; }
    public DbSet<PaymentEvent> PaymentEvents { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentEventConfiguration());
        
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
} 