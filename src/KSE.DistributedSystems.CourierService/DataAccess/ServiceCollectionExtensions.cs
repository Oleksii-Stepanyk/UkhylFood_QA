using KSE.DistributedSystems.CourierService.DataAccess.Interfaces;
using KSE.DistributedSystems.CourierService.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KSE.DistributedSystems.CourierService.DataAccess;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataAccessServices(this IServiceCollection services)
    {
        services.AddDbContext<CourierDbContext>((serviceProvider, options) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString("DbContext");
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<ICourierRepository, CourierRepository>();

        return services;
    }
}