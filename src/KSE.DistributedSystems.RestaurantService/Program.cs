using System;
using System.Threading.Tasks;
using KSE.DistributedSystems.RestaurantService.Application.Consumers;
using KSE.DistributedSystems.RestaurantService.Application.Services;
using KSE.DistributedSystems.RestaurantService.DataAccess;
using KSE.DistributedSystems.RestaurantService.DataAccess.Repositories;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace KSE.DistributedSystems.RestaurantService;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((ctx, config) => config.ReadFrom.Configuration(ctx.Configuration));

        builder.Services.AddControllers();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.Services.AddScoped<IOrderService, Application.Services.OrderService>();
        builder.Services.AddScoped<IOrderRepository, OrderRepository>();
        builder.Services.AddSingleton<OrderMonitoringService>();

        builder.Services.AddAutoMapper(
            typeof(Application.MappingProfiles.OrderProfile));

        builder.Services.AddMassTransit(x =>
        {
            x.AddConsumer<OrderConsumer>();

            x.AddEntityFrameworkOutbox<RestaurantDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(5);
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(new Uri(builder.Configuration["RabbitMQ:Host"] ??
                                 throw new ArgumentNullException("RabbitMQ host is not configured")));

                cfg.ReceiveEndpoint("order.restaurant", e =>
                {
                    e.ConfigureConsumer<OrderConsumer>(context);
                    e.UseDelayedRedelivery(r => r.Intervals(
                        TimeSpan.FromMinutes(1),
                        TimeSpan.FromMinutes(5),
                        TimeSpan.FromMinutes(15)));
                    e.UseMessageRetry(r => r.Immediate(3));
                    e.UseEntityFrameworkOutbox<RestaurantDbContext>(context);
                });
            });
        });

        if (!builder.Environment.IsEnvironment("Testing"))
        {
            builder.Services.AddDbContext<RestaurantDbContext>(options =>
            {
                options.UseNpgsql(builder.Configuration.GetConnectionString("RestaurantServiceDb"));
            });
        }

        var tracingOtlpEndpoint = builder.Configuration["OTLP_ENDPOINT_URL"];
        var otel = builder.Services.AddOpenTelemetry();

        otel.ConfigureResource(resource => resource
            .AddService(serviceName: builder.Environment.ApplicationName));

        otel.WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation()
            .AddMeter("Microsoft.AspNetCore.Hosting")
            .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
            .AddMeter("System.Net.Http")
            .AddMeter("System.Net.NameResolution")
            .AddMeter("KSE.DistributedSystems.RestaurantService")
            .AddPrometheusExporter()
        );

        otel.WithTracing(tracing =>
        {
            tracing.AddAspNetCoreInstrumentation();
            tracing.AddHttpClientInstrumentation();
            tracing.AddEntityFrameworkCoreInstrumentation();
            tracing.AddSource("MassTransit");
            tracing.AddSource("MessageHandlerFactory");
            tracing.AddSource("KSE.DistributedSystems.RestaurantService");

            if (tracingOtlpEndpoint != null)
            {
                tracing.AddOtlpExporter(otlpOptions => { otlpOptions.Endpoint = new Uri(tracingOtlpEndpoint); });
            }
            else
            {
                tracing.AddConsoleExporter();
            }
        });

        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .Enrich.WithEnvironmentName()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{MessageId}] {Message:lj}{NewLine}{Exception}")
            .MinimumLevel.Debug()
            .CreateLogger();


        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
            options.GetLevel = (_, elapsed, ex) => ex != null
                ? Serilog.Events.LogEventLevel.Error
                : elapsed > 1000
                    ? Serilog.Events.LogEventLevel.Warning
                    : Serilog.Events.LogEventLevel.Information;

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
                diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
                diagnosticContext.Set("RemoteIP", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            };
        });

        app.MapGet("test-metrics", ([FromServices] OrderMonitoringService monitoring) =>
        {
            monitoring.IncrementOrderProcessed("test", Random.Shared.Next(2) == 0 ? "success" : "failure");
            monitoring.RecordOrderProcessingDuration("test", Random.Shared.NextDouble() * 1000,
                Random.Shared.Next(2) == 0);
            return Results.Ok("Test metrics recorded");
        });

        if (!app.Environment.IsEnvironment("Testing"))
        {
            await using var scope = app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();
            await db.Database.MigrateAsync();
        }

        app.UseHttpsRedirection();

        app.UseAuthorization();

        app.MapControllers();

        app.MapPrometheusScrapingEndpoint();

        app.MapGet("/health", () => Results.Ok("Restaurant service response"));

        await app.RunAsync();
    }
}