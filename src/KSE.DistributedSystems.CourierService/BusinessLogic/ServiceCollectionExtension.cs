using KSE.DistributedSystems.CourierService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.CourierService.BusinessLogic.MappingProfiles;
using Microsoft.Extensions.DependencyInjection;

namespace KSE.DistributedSystems.CourierService.BusinessLogic;

public static class ServiceCollectionExtension
{
    public static IServiceCollection AddBusinessLogicServices(this IServiceCollection services)
    {
        services.AddAutoMapper(cfg => { cfg.AddProfile<CourierProfile>(); });

        services.AddScoped<ICourierService, Services.CourierService>();
        services.Decorate<ICourierService, Services.CourierServiceWithCache>();

        return services;
    }
}