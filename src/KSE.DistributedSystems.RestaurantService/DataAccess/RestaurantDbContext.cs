using System.Reflection;
using KSE.DistributedSystems.RestaurantService.DataAccess.Models;
using Microsoft.EntityFrameworkCore;
using MassTransit;

namespace KSE.DistributedSystems.RestaurantService.DataAccess;

public class RestaurantDbContext(DbContextOptions<RestaurantDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}