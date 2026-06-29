using KSE.DistributedSystems.PaymentService.DataAccess.Entities;

namespace KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;

public class PaymentResponseDto
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus Status { get; set; }
    public string? ExternalPaymentId { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public PaymentMetadataDto Metadata { get; set; } = new();
}

public class PaymentMetadataDto
{
    public string? CardLast4 { get; set; }
    public string? CardBrand { get; set; }
    public Dictionary<string, string> AdditionalData { get; set; } = new();
} 