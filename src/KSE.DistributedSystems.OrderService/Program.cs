using KSE.DistributedSystems.OrderService.Consumers;
using KSE.DistributedSystems.OrderService.DataAccess;
using KSE.DistributedSystems.OrderService.DataAccess.Repositories;
using KSE.DistributedSystems.OrderService.Services;
using KSE.DistributedSystems.OrderService.Services.Interfaces;
using KSE.DistributedSystems.Shared;
using KSE.DistributedSystems.Shared.Middleware;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    Log.Information("Starting Order Service");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, config) => config.ReadFrom.Configuration(ctx.Configuration));

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddResilienceServices(builder.Configuration);

    builder.Services.AddSingleton<OrderMonitoringService>();

    builder.Services.AddScoped<IOrderService, OrderService>();
    builder.Services.AddScoped<IOrderRepository, OrderRepository>();
    builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();
    builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();

    Log.Information("Configuring MassTransit and RabbitMQ");

    builder.Services.AddMassTransit(x =>
    {
        x.AddConsumer<CustomerOrderConsumer>();
        x.AddConsumer<PaymentResultConsumer>();

        x.AddEntityFrameworkOutbox<OrderDbContext>(o =>
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

            cfg.ReceiveEndpoint("order.from-customer", e =>
            {
                e.ConfigureConsumer<CustomerOrderConsumer>(context);
                e.UseDelayedRedelivery(r => r.Intervals(
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(15)));
                e.UseMessageRetry(r => r.Immediate(3));
                e.UseEntityFrameworkOutbox<OrderDbContext>(context);
            });

            // Removed courier.order and order.from-restaurant endpoints

            cfg.ReceiveEndpoint("payment.response", e =>
            {
                e.ConfigureConsumer<PaymentResultConsumer>(context);
                e.UseDelayedRedelivery(r => r.Intervals(
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(15)));
                e.UseMessageRetry(r => r.Immediate(3));
                e.UseEntityFrameworkOutbox<OrderDbContext>(context);
            });
        });
    });

    if (!builder.Environment.IsEnvironment("Testing"))
    {
        builder.Services.AddDbContext<OrderDbContext>(options =>
        {
            options.UseNpgsql(builder.Configuration.GetConnectionString("OrderServiceDb"));
        });
    }

    Log.Information("Configuring Matrics and Tracing");

    builder.Services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy("Order service is running"));

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
        .AddMeter("KSE.DistributedSystems.OrderService")
        .AddPrometheusExporter()
    );

    otel.WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        tracing.AddEntityFrameworkCoreInstrumentation();
        tracing.AddSource("MassTransit");
        tracing.AddSource("MessageHandlerFactory");
        tracing.AddSource("KSE.DistributedSystems.OrderService");

        if (tracingOtlpEndpoint != null)
        {
            tracing.AddOtlpExporter(otlpOptions => { otlpOptions.Endpoint = new Uri(tracingOtlpEndpoint); });
        }
        else
        {
            tracing.AddConsoleExporter();
        }
    });

    var app = builder.Build();

    Log.Information("Order Service configured, starting middleware pipeline");

    app.UseMiddleware<GlobalExceptionHandlingMiddleware>();

    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.GetLevel = (httpContext, elapsed, ex) => ex != null
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
    }

    Log.Information("Applying database migrations");

    if (!app.Environment.IsEnvironment("Testing"))
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        await db.Database.MigrateAsync();
    }

    app.UseHttpsRedirection();

    app.UseAuthorization();

    app.MapControllers();

    app.MapGet("test", () => Results.Ok("response from order service"));

    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready");
    app.MapHealthChecks("/health/live");

    app.MapPrometheusScrapingEndpoint();

    app.MapGet("test-metrics", (OrderMonitoringService monitoring) =>
    {
        monitoring.IncrementOrderProcessed("test", Random.Shared.Next(2) == 0 ? "success" : "failure");
        monitoring.RecordOrderProcessingDuration("test", Random.Shared.NextDouble() * 1000, Random.Shared.Next(2) == 0);
        return Results.Ok("Test metrics recorded");
    });

    Log.Information("Order Service configured successfully");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Order Service terminated unexpectedly");
    throw;
}
finally
{
    Log.Information("Order Service shutting down");
    Log.CloseAndFlush();
}

public partial class Program;