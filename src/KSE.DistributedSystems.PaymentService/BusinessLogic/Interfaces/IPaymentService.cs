using KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;

namespace KSE.DistributedSystems.PaymentService.BusinessLogic.Interfaces;

public interface IPaymentService
{
    Task<PaymentResponseDto> ProcessPaymentAsync(PaymentRequestDto request);
    Task<PaymentResponseDto?> GetPaymentAsync(Guid paymentId);
    Task<PaymentResponseDto?> GetPaymentByOrderIdAsync(Guid orderId);
    Task<IEnumerable<PaymentResponseDto>> GetCustomerPaymentsAsync(Guid customerId);
    Task<PaymentResponseDto?> RefundPaymentAsync(RefundRequestDto request);
    Task<PaymentResponseDto?> CancelPaymentAsync(Guid paymentId);
    Task<IEnumerable<PaymentResponseDto>> GetPaymentHistoryAsync(Guid customerId, DateTime? from = null, DateTime? to = null);
    Task<bool> UpdatePaymentStatusAsync(Guid paymentId, PaymentStatus status, string? reason = null);
}

public interface IPaymentProcessor
{
    Task<PaymentProcessingResult> ProcessAsync(Payment payment);
    Task<PaymentProcessingResult> RefundAsync(Payment payment, decimal? amount = null);
    Task<PaymentProcessingResult> CancelAsync(Payment payment);
}

public class PaymentProcessingResult
{
    public bool IsSuccess { get; set; }
    public string? ExternalPaymentId { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> ProcessorResponse { get; set; } = new();
} 