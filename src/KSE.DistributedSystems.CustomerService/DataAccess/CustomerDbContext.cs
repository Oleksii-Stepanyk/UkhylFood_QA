using KSE.DistributedSystems.CustomerService.DataAccess.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace KSE.DistributedSystems.CustomerService.DataAccess;

public class CustomerDbContext(DbContextOptions<CustomerDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers { get; set; }
    public DbSet<PaymentMethod> PaymentMethods { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<Customer>()
            .HasMany(c => c.PaymentMethods)
            .WithOne(pm => pm.Customer)
            .HasForeignKey(pm => pm.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
        
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}