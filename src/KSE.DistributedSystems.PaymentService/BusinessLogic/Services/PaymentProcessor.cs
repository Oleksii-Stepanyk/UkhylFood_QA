using KSE.DistributedSystems.PaymentService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;

namespace KSE.DistributedSystems.PaymentService.BusinessLogic.Services;

public class PaymentProcessor : IPaymentProcessor
{
    private readonly ILogger<PaymentProcessor> _logger;
    private readonly Random _random = new();

    public PaymentProcessor(ILogger<PaymentProcessor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PaymentProcessingResult> ProcessAsync(Payment payment)
    {
        _logger.LogInformation("Processing payment {PaymentId} for amount {Amount} {Currency}", 
            payment.Id, payment.Amount, payment.Currency);
        
        await Task.Delay(TimeSpan.FromMilliseconds(_random.Next(500, 2000)));
        
        var result = new PaymentProcessingResult();

        if (payment.Amount > 5000)
        {
            result.IsSuccess = _random.NextDouble() > 0.3;
        }
        else if (payment.Amount < 1)
        {
            result.IsSuccess = false;
            result.ErrorMessage = "Amount too small for processing";
        }
        else
        {
            result.IsSuccess = _random.NextDouble() > 0.05;
        }

        if (result.IsSuccess)
        {
            result.ExternalPaymentId = $"ext_{Guid.NewGuid():N}";
            result.ProcessorResponse["transaction_id"] = result.ExternalPaymentId;
            result.ProcessorResponse["processor"] = GetProcessorName(payment.PaymentMethod);
            result.ProcessorResponse["authorization_code"] = GenerateAuthCode();
            
            _logger.LogInformation("Payment {PaymentId} processed successfully with external ID {ExternalId}", 
                payment.Id, result.ExternalPaymentId);
        }
        else
        {
            result.ErrorMessage ??= GetRandomFailureReason();
            result.ProcessorResponse["error_code"] = GetRandomErrorCode();
            result.ProcessorResponse["processor"] = GetProcessorName(payment.PaymentMethod);
            
            _logger.LogWarning("Payment {PaymentId} failed: {Error}", payment.Id, result.ErrorMessage);
        }

        return result;
    }

    public async Task<PaymentProcessingResult> RefundAsync(Payment payment, decimal? amount = null)
    {
        _logger.LogInformation("Processing refund for payment {PaymentId}, amount: {Amount}", 
            payment.Id, amount ?? payment.Amount);
        
        await Task.Delay(TimeSpan.FromMilliseconds(_random.Next(300, 1000)));

        var result = new PaymentProcessingResult
        {
            IsSuccess = _random.NextDouble() > 0.02,
            ProcessorResponse = new Dictionary<string, string>
            {
                ["processor"] = GetProcessorName(payment.PaymentMethod),
                ["original_transaction"] = payment.ExternalPaymentId ?? "unknown"
            }
        };

        if (result.IsSuccess)
        {
            result.ExternalPaymentId = $"ref_{Guid.NewGuid():N}";
            result.ProcessorResponse["refund_id"] = result.ExternalPaymentId;
            
            _logger.LogInformation("Refund for payment {PaymentId} processed successfully", payment.Id);
        }
        else
        {
            result.ErrorMessage = "Refund processing failed - please contact support";
            result.ProcessorResponse["error_code"] = "REFUND_FAILED";
            
            _logger.LogWarning("Refund for payment {PaymentId} failed", payment.Id);
        }

        return result;
    }

    public async Task<PaymentProcessingResult> CancelAsync(Payment payment)
    {
        _logger.LogInformation("Cancelling payment {PaymentId}", payment.Id);

        // Simulate processing delay
        await Task.Delay(TimeSpan.FromMilliseconds(_random.Next(100, 500)));

        var result = new PaymentProcessingResult
        {
            IsSuccess = true,
            ProcessorResponse = new Dictionary<string, string>
            {
                ["processor"] = GetProcessorName(payment.PaymentMethod),
                ["cancellation_reason"] = "Customer requested cancellation"
            }
        };

        _logger.LogInformation("Payment {PaymentId} cancelled successfully", payment.Id);

        return result;
    }

    private static string GetProcessorName(PaymentMethod method) => method switch
    {
        PaymentMethod.CreditCard or PaymentMethod.DebitCard => "Stripe",
        PaymentMethod.PayPal => "PayPal",
        PaymentMethod.ApplePay => "Apple Pay",
        PaymentMethod.GooglePay => "Google Pay",
        PaymentMethod.BankTransfer => "ACH",
        PaymentMethod.Cash => "Cash",
        _ => "Unknown"
    };

    private string GenerateAuthCode() => _random.Next(100000, 999999).ToString();

    private string GetRandomErrorCode() => _random.Next(0, 5) switch
    {
        0 => "INSUFFICIENT_FUNDS",
        1 => "CARD_DECLINED",
        2 => "EXPIRED_CARD",
        3 => "INVALID_CVV",
        _ => "PROCESSING_ERROR"
    };

    private string GetRandomFailureReason() => _random.Next(0, 5) switch
    {
        0 => "Insufficient funds",
        1 => "Card declined by issuer",
        2 => "Card has expired",
        3 => "Invalid CVV code",
        _ => "Payment processing error"
    };
} 