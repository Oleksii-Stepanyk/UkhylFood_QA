using System;
using KSE.DistributedSystems.CourierService.BusinessLogic;
using KSE.DistributedSystems.CourierService.BusinessLogic.Consumers;
using KSE.DistributedSystems.CourierService.DataAccess;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using StackExchange.Redis;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
Log.Information("Starting Courier Service");

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, config) => config.ReadFrom.Configuration(ctx.Configuration));

var serviceName = builder.Configuration["ServiceName"] ?? "KSE.DistributedSystems.CourierService";

var tracingOtlpEndpoint = builder.Configuration["OTLP_ENDPOINT_URL"];

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddProcessInstrumentation()
        .AddMeter("Microsoft.AspNetCore.Hosting")
        .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
        .AddMeter("System.Net.Http")
        .AddMeter("System.Net.NameResolution")
        .AddMeter("CourierService.Metrics")
        .AddPrometheusExporter()
    )
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddNpgsql()
            .AddSource(serviceName)
            .AddSource("MassTransit");

        if (tracingOtlpEndpoint != null)
        {
            tracing.AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri(tracingOtlpEndpoint);
                otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
            });
        }
        else
        {
            tracing.AddConsoleExporter();
        }
    });

builder.Logging.AddOpenTelemetry(options => options
    .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName))
    .AddConsoleExporter()
);

builder.Services.AddSingleton(TracerProvider.Default.GetTracer(serviceName));

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<RestaurantOrderConsumer>();
    x.AddConsumer<OrderConsumer>();

    x.AddEntityFrameworkOutbox<CourierDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
        o.QueryDelay = TimeSpan.FromSeconds(5);
        o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(new Uri(builder.Configuration["RabbitMQ:Host"]
            ?? throw new ArgumentNullException("RabbitMQ host is not configured")));

        cfg.ReceiveEndpoint("courier.from-restaurant", e =>
        {
            e.ConfigureConsumer<RestaurantOrderConsumer>(context);
            e.UseDelayedRedelivery(r => r.Intervals(
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(15)));
            e.UseMessageRetry(r => r.Immediate(3));
            e.UseEntityFrameworkOutbox<CourierDbContext>(context);
        });

        cfg.ReceiveEndpoint("courier.from-order", e =>
        {
            e.ConfigureConsumer<OrderConsumer>(context);
            e.UseDelayedRedelivery(r => r.Intervals(
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(15)));
            e.UseMessageRetry(r => r.Immediate(3));
            e.UseEntityFrameworkOutbox<CourierDbContext>(context);
        });
    });
});

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var configuration = builder.Configuration.GetConnectionString("Redis");
    return ConnectionMultiplexer.Connect(configuration!);
});

builder.Services.AddDbContext<CourierDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DbContext");
    options.UseNpgsql(connectionString);
});

builder.Services.AddDataAccessServices();
builder.Services.AddBusinessLogicServices();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CourierDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapPrometheusScrapingEndpoint();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok("Courier service response"));

Log.Information("Courier Service configured successfully");

await app.RunAsync();