using KSE.DistributedSystems.Shared.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace KSE.DistributedSystems.Shared;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddResilienceServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ResilienceOptions>(configuration.GetSection(ResilienceOptions.SectionName));
        services.AddSingleton<ResiliencePolicies>();
        
        return services;
    }

    public static IServiceCollection AddResilientHttpClient<TInterface, TImplementation>(
        this IServiceCollection services, 
        string clientName,
        string baseAddress,
        IConfiguration configuration)
        where TInterface : class
        where TImplementation : class, TInterface
    {
        services.AddHttpClient<TInterface, TImplementation>(clientName, client =>
        {
            client.BaseAddress = new Uri(baseAddress);
            client.Timeout = TimeSpan.FromSeconds(60);
        });

        return services;
    }

    public static IServiceCollection AddServiceHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var healthChecksBuilder = services.AddHealthChecks();

        healthChecksBuilder.AddCheck("self", () => HealthCheckResult.Healthy());
        
        return services;
    }
} 