using System.Diagnostics.Metrics;

namespace KSE.DistributedSystems.PaymentService.BusinessLogic.Services;

public sealed class PaymentMonitoringService : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<int> _paymentProcessedCounter;
    private readonly Counter<int> _paymentStatusCounter;
    private readonly Histogram<double> _paymentProcessingDuration;
    private readonly ILogger<PaymentMonitoringService> _logger;

    public PaymentMonitoringService(ILogger<PaymentMonitoringService> logger)
    {
        _logger = logger;
        _meter = new Meter("KSE.DistributedSystems.PaymentService");

        _paymentProcessedCounter = _meter.CreateCounter<int>(
            name: "payment_processed_total",
            description: "Total number of payments processed by operation and result");

        _paymentStatusCounter = _meter.CreateCounter<int>(
            name: "payment_status_total", 
            description: "Total number of payments by status (successful/failed)");

        _paymentProcessingDuration = _meter.CreateHistogram<double>(
            name: "payment_processing_duration_ms",
            description: "Duration of payment processing operations in milliseconds",
            unit: "ms");

        _logger.LogInformation("PaymentMonitoringService initialized with meter name: {MeterName}", _meter.Name);
    }

    public void IncrementPaymentProcessed(string operation, string result, string? paymentMethod = null)
    {
        try
        {
            var tags = new List<KeyValuePair<string, object?>>
            {
                new("operation", operation),
                new("result", result)
            };

            if (!string.IsNullOrEmpty(paymentMethod))
            {
                tags.Add(new("payment_method", paymentMethod));
            }

            _paymentProcessedCounter.Add(1, [.. tags]);

            _logger.LogDebug("Incremented payment processed counter: operation={Operation}, result={Result}, paymentMethod={PaymentMethod}",
                operation, result, paymentMethod);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error incrementing payment processed counter");
        }
    }

    public void IncrementPaymentStatus(string status, string? paymentMethod = null, decimal? amount = null)
    {
        try
        {
            var tags = new List<KeyValuePair<string, object?>>
            {
                new("status", status)
            };

            if (!string.IsNullOrEmpty(paymentMethod))
            {
                tags.Add(new("payment_method", paymentMethod));
            }

            if (amount.HasValue)
            {
                if (amount.Value <= 100)
                    tags.Add(new("amount_range", "small"));
                else if (amount.Value <= 1000)
                    tags.Add(new("amount_range", "medium"));
                else
                    tags.Add(new("amount_range", "large"));
            }

            _paymentStatusCounter.Add(1, [.. tags]);

            _logger.LogDebug("Incremented payment status counter: status={Status}, paymentMethod={PaymentMethod}, amount={Amount}",
                status, paymentMethod, amount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error incrementing payment status counter");
        }
    }

    public void RecordPaymentProcessingDuration(string operation, double durationMs, bool success = true, string? paymentMethod = null)
    {
        try
        {
            var tags = new List<KeyValuePair<string, object?>>
            {
                new("operation", operation),
                new("success", success.ToString().ToLower())
            };

            if (!string.IsNullOrEmpty(paymentMethod))
            {
                tags.Add(new("payment_method", paymentMethod));
            }

            _paymentProcessingDuration.Record(durationMs, [.. tags]);

            _logger.LogDebug("Recorded payment processing duration: operation={Operation}, duration={Duration}ms, success={Success}, paymentMethod={PaymentMethod}",
                operation, durationMs, success, paymentMethod);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording payment processing duration");
        }
    }

    public void Dispose()
    {
        _meter?.Dispose();
        _logger.LogInformation("PaymentMonitoringService disposed");
    }
} 