using KSE.DistributedSystems.PaymentService.BusinessLogic;
using KSE.DistributedSystems.PaymentService.DataAccess;
using MassTransit;
using Microsoft.OpenApi.Models;
using Serilog;
using StackExchange.Redis;
using FluentValidation.AspNetCore;
using System.Reflection;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using KSE.DistributedSystems.PaymentService.API;
using System.Diagnostics;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Consumers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Starting Payment Service");
    
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => 
        configuration.ReadFrom.Configuration(context.Configuration));

    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (!string.IsNullOrEmpty(connectionString))
    {
        builder.Services.AddPostgreSqlDataAccessServices(connectionString);
    }
    else
    {
        builder.Services.AddDbContext<PaymentDbContext>(options =>
            options.UseInMemoryDatabase("PaymentServiceDb")
                   .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
        builder.Services.AddDataAccessServices();
    }

    builder.Services.AddBusinessLogicServices();

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    builder.Services.ConfigureOptions<ConfigureSwaggerOptions>();
    builder.Services.AddSwaggerGen(c =>
    {
        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            c.IncludeXmlComments(xmlPath);
        }

        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer"
        });
    });

    builder.Services.AddFluentValidationAutoValidation();

    builder.Services.AddMassTransit(x =>
    {
        x.AddConsumer<OrderConsumer>();

        if (!string.IsNullOrEmpty(connectionString))
        {
            x.AddEntityFrameworkOutbox<PaymentDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox(); 
                o.QueryDelay = TimeSpan.FromSeconds(5);
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(30);
            });
        }

        x.UsingRabbitMq((context, cfg) =>
        {
            cfg.Host(new Uri(builder.Configuration["RabbitMQ:Host"]
                ?? throw new ArgumentNullException("RabbitMQ host is not configured")));

            cfg.ReceiveEndpoint("payment.order", e => 
            { 
                e.ConfigureConsumer<OrderConsumer>(context);
                
                e.UseDelayedRedelivery(r => r.Intervals(
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(15)));
                e.UseMessageRetry(r => r.Immediate(3));

                if (!string.IsNullOrEmpty(connectionString))
                {
                    e.UseEntityFrameworkOutbox<PaymentDbContext>(context);
                }
                else
                {
                    e.UseInMemoryOutbox(context);
                }
            });
        });
    });

    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    {
        var configuration = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
        return ConnectionMultiplexer.Connect(configuration);
    });

    builder.Services.AddHealthChecks()
        .AddCheck("redis", () => HealthCheckResult.Healthy());

    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
    });

    builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new QueryStringApiVersionReader("version"),
            new HeaderApiVersionReader("X-Version"),
            new MediaTypeApiVersionReader("ver")
        );
    }).AddApiExplorer(setup =>
    {
        setup.GroupNameFormat = "'v'VVV";
        setup.SubstituteApiVersionInUrl = true;
    });

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
        .AddMeter("KSE.DistributedSystems.PaymentService")
        .AddPrometheusExporter()
    );

    otel.WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddHttpClientInstrumentation();
        tracing.AddEntityFrameworkCoreInstrumentation();
        tracing.AddSource("MassTransit");
        tracing.AddSource("MessageHandlerFactory");
        tracing.AddSource("KSE.DistributedSystems.PaymentService");

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

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
            foreach (var description in provider.ApiVersionDescriptions)
            {
                c.SwaggerEndpoint($"/swagger/{description.GroupName}/swagger.json", 
                    $"Payment Service API {description.GroupName.ToUpperInvariant()}");
            }
        });
    }

    app.UseHttpsRedirection();

    app.UseCors();

    app.UseSerilogRequestLogging();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/ready");
    app.MapHealthChecks("/health/live");

    app.MapGet("/health/status", () => Results.Ok(new
    {
        Service = "Payment Service",
        Status = "Healthy",
        Timestamp = DateTime.UtcNow,
        Version = "1.0.0"
    }));

    app.MapGet("/info", () => Results.Ok(new
    {
        Service = "Payment Service",
        Uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime,
        Environment = app.Environment.EnvironmentName,
        MachineName = Environment.MachineName
    }));

    app.MapGet("test-metrics", (KSE.DistributedSystems.PaymentService.BusinessLogic.Services.PaymentMonitoringService monitoring) =>
    {
        var random = new Random();
        var paymentMethods = new[] { "CreditCard", "DebitCard", "PayPal", "ApplePay" };
        
        for (int i = 0; i < 3; i++)
        {
            var method = paymentMethods[random.Next(paymentMethods.Length)];
            var isSuccess = random.NextDouble() > 0.4;
            var amount = (decimal)(random.NextDouble() * 1000 + 10);
            
            if (isSuccess)
            {
                monitoring.IncrementPaymentStatus("success", method, amount);
                monitoring.IncrementPaymentProcessed("test_payment", "success", method);
            }
            else
            {
                monitoring.IncrementPaymentStatus("failed", method, amount);
                monitoring.IncrementPaymentProcessed("test_payment", "failed", method);
            }
            
            monitoring.RecordPaymentProcessingDuration("test_payment", 
                random.NextDouble() * 2000 + 100, isSuccess, method);
        }
        
        return Results.Ok("Test payment metrics recorded");
    });
    
    app.MapPrometheusScrapingEndpoint();

    Log.Information("Payment Service configured successfully");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Payment Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}