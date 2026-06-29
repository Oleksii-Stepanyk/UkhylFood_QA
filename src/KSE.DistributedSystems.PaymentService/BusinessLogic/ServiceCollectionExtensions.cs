using FluentValidation;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.PaymentService.BusinessLogic.MappingProfiles;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Validators;
using KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;

namespace KSE.DistributedSystems.PaymentService.BusinessLogic;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBusinessLogicServices(this IServiceCollection services)
    {
        services.AddAutoMapper(cfg => { cfg.AddProfile<PaymentProfile>(); });
        services.AddScoped<IPaymentService, Services.PaymentService>();
        services.AddScoped<IPaymentProcessor, Services.PaymentProcessor>();
        services.AddScoped<IValidator<PaymentRequestDto>, PaymentRequestValidator>();
        services.AddScoped<IValidator<RefundRequestDto>, RefundRequestValidator>();
        services.AddSingleton<Services.PaymentMonitoringService>();
        services.AddMemoryCache();

        return services;
    }
} 