using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace KSE.DistributedSystems.PaymentService.API;

public class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{
    private readonly IApiVersionDescriptionProvider _provider;

    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
    {
        _provider = provider;
    }

    public void Configure(SwaggerGenOptions options)
    {
        foreach (var description in _provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(description.GroupName, CreateInfoForApiVersion(description));
        }
    }

    private static OpenApiInfo CreateInfoForApiVersion(ApiVersionDescription description)
    {
        var info = new OpenApiInfo
        {
            Title = "Payment Service API",
            Version = description.ApiVersion.ToString(),
            Description = "A comprehensive payment processing service for the food delivery platform",
            Contact = new OpenApiContact
            {
                Name = "Payment Service Team",
                Email = "payments@fooddelivery.com"
            }
        };

        if (description.IsDeprecated)
        {
            info.Description += " - DEPRECATED";
        }

        return info;
    }
} 