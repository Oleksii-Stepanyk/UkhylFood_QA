using System.Reflection;
using KSE.DistributedSystems.CourierService.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using MassTransit;

namespace KSE.DistributedSystems.CourierService.DataAccess;

public class CourierDbContext(DbContextOptions<CourierDbContext> options) : DbContext(options)
{
    public DbSet<Courier> Couriers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}