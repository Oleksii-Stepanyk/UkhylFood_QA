using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace KSE.DistributedSystems.RestaurantService.Application.Services;

public sealed class OrderMonitoringService : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<int> _orderProcessedCounter;
    private readonly Histogram<double> _orderProcessingDuration;
    private readonly ILogger<OrderMonitoringService> _logger;

    public OrderMonitoringService(ILogger<OrderMonitoringService> logger)
    {
        _logger = logger;
        _meter = new Meter("KSE.DistributedSystems.RestaurantService");

        _orderProcessedCounter = _meter.CreateCounter<int>(
            name: "order_processed_total",
            description: "Total number of orders processed by operation and status");

        _orderProcessingDuration = _meter.CreateHistogram<double>(
            name: "order_processing_duration_ms",
            description: "Duration of order processing operations in milliseconds",
            unit: "ms");

        _logger.LogInformation("OrderMonitoringService initialized with meter name: {MeterName}", _meter.Name);
    }

    public void IncrementOrderProcessed(string operation, string orderStatus)
    {
        try
        {
            var tags = new List<KeyValuePair<string, object?>>
            {
                new("operation", operation),
                new("status", orderStatus)
            };

            _orderProcessedCounter.Add(1, [.. tags]);

            _logger.LogDebug("Incremented order processed counter: operation={Operation}, status={Status}",
                operation, orderStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error incrementing order processed counter");
        }
    }

    public void RecordOrderProcessingDuration(string operation, double durationMs, bool success = true)
    {
        try
        {
            var tags = new KeyValuePair<string, object?>[]
            {
                new("operation", operation),
                new("success", success.ToString().ToLower())
            };

            _orderProcessingDuration.Record(durationMs, tags);

            _logger.LogDebug("Recorded order processing duration: operation={Operation}, duration={Duration}ms, success={Success}",
                operation, durationMs, success);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording order processing duration");
        }
    }

    public void Dispose()
    {
        _meter?.Dispose();
        _logger.LogInformation("OrderMonitoringService disposed");
    }
   
}