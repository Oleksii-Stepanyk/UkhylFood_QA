using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using KSE.DistributedSystems.OrderService.Services;
using Microsoft.Extensions.Logging;

namespace KSE.DistributedSystems.CustomerService.Services;

public sealed class CustomerMonitoringService : IDisposable
{
    private readonly Meter _meter;
    private readonly Histogram<double> _orderProcessingDuration;
    private readonly ILogger<CustomerMonitoringService> _logger;

    public CustomerMonitoringService(ILogger<CustomerMonitoringService> logger)
    {
        _logger = logger;
        _meter = new Meter("KSE.DistributedSystems.СustomerService");

        _orderProcessingDuration = _meter.CreateHistogram<double>(
            name: "customer_processing_duration_ms",
            description: "Duration of customers processing operations in milliseconds",
            unit: "ms");

        _logger.LogInformation("CustomerMonitoringService initialized with meter name: {MeterName}", _meter.Name);
    }

    public void RecordCustomerProcessingDuration(string operation, double durationMs, bool success = true)
    {
        try
        {
            var tags = new KeyValuePair<string, object?>[]
            {
                new("operation", operation),
                new("success", success.ToString().ToLower())
            };

            _orderProcessingDuration.Record(durationMs, tags);

            _logger.LogDebug(
                "Recorded customer processing duration: operation={Operation}, duration={Duration}ms, success={Success}",
                operation, durationMs, success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording customer processing duration");
        }
    }

    public void Dispose()
    {
        _meter?.Dispose();
        _logger.LogInformation("OrderMonitoringService disposed");
    }
}