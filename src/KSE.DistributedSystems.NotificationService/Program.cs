using KSE.DistributedSystems.NotificationService.Consumers;
using KSE.DistributedSystems.NotificationService.Models;
using KSE.DistributedSystems.NotificationService.Services;
using KSE.DistributedSystems.Shared;
using KSE.DistributedSystems.Shared.Middleware;
using MassTransit;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using System.Net.Http.Headers;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    Log.Information("Starting Notification Service");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, config) => config.ReadFrom.Configuration(ctx.Configuration));



    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddResilienceServices(builder.Configuration);
    builder.Services.Configure<SendGridSettings>(builder.Configuration.GetSection("SendGrid"));

    builder.Services.AddHttpClient<IEmailService, EmailService>("SendGrid", client =>
    {
        client.BaseAddress = new Uri("https://api.sendgrid.com/");
        client.Timeout = TimeSpan.FromSeconds(30);
    });

    builder.Services.AddMassTransit(x =>
    {
        x.AddConsumer<OrderCreatedConsumer>();
        x.AddConsumer<OrderDeliveredConsumer>();
        x.AddConsumer<PaymentSuccessfulConsumer>();
        x.AddConsumer<PaymentFailedConsumer>();

        x.UsingRabbitMq((context, cfg) =>
        {
            var rabbitMqHost = builder.Configuration["RabbitMQ:Host"]
                ?? throw new InvalidOperationException("RabbitMQ host is not configured.");

            cfg.Host(new Uri(rabbitMqHost));

            cfg.ReceiveEndpoint("order.created", e =>
            {
                e.ConfigureConsumer<OrderCreatedConsumer>(context);
                e.UseDelayedRedelivery(r => r.Intervals(
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(15)));
                e.UseMessageRetry(r => r.Immediate(3));
            });

            cfg.ReceiveEndpoint("order.delivered", e =>
            {
                e.ConfigureConsumer<OrderDeliveredConsumer>(context);
                e.UseDelayedRedelivery(r => r.Intervals(
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(15)));
                e.UseMessageRetry(r => r.Immediate(3));
            });

            cfg.ReceiveEndpoint("payment.successful", e =>
            {
                e.ConfigureConsumer<PaymentSuccessfulConsumer>(context);
                e.UseDelayedRedelivery(r => r.Intervals(
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(15)));
                e.UseMessageRetry(r => r.Immediate(3));
            });

            cfg.ReceiveEndpoint("payment.failed", e =>
            {
                e.ConfigureConsumer<PaymentFailedConsumer>(context);
                e.UseDelayedRedelivery(r => r.Intervals(
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(15)));
                e.UseMessageRetry(r => r.Immediate(3));
            });
        });
    });

    builder.Services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy("Notification service is running"))
        .AddCheck<SendGridHealthCheck>("SendGrid API");

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
        .AddPrometheusExporter()
    );

    otel.WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        tracing.AddSource("MassTransit");
        tracing.AddSource("MessageHandlerFactory");
        tracing.AddSource("KSE.DistributedSystems.NotificationService.EmailService");

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
        app.MapControllers();
    }

    app.UseHttpsRedirection();

    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready");
    app.MapHealthChecks("/health/live");

    app.MapPrometheusScrapingEndpoint();

    Log.Information("Notification Service configured successfully");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Notification Service terminated unexpectedly");
    throw;
}
finally
{
    Log.Information("Notification Service shutting down");
    Log.CloseAndFlush();
}