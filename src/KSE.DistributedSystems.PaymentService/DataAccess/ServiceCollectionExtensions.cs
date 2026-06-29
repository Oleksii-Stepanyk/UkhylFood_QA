using Microsoft.EntityFrameworkCore;
using KSE.DistributedSystems.PaymentService.DataAccess.Interfaces;
using KSE.DistributedSystems.PaymentService.DataAccess.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace KSE.DistributedSystems.PaymentService.DataAccess;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataAccessServices(this IServiceCollection services)
    {
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        return services;
    }

    public static IServiceCollection AddInMemoryDataAccessServices(this IServiceCollection services)
    {
        services.AddDbContext<PaymentDbContext>(options =>
            options.UseInMemoryDatabase($"PaymentServiceDb_{Guid.NewGuid()}"));

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        return services;
    }

    public static IServiceCollection AddPostgreSqlDataAccessServices(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<PaymentDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        return services;
    }
} 