namespace KSE.DistributedSystems.PaymentService.DataAccess.Entities;

public class Payment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus Status { get; set; }
    public string? ExternalPaymentId { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? RefundedAt { get; set; }
    public PaymentMetadata Metadata { get; set; } = new();
    
    // Navigation properties for audit trail
    public List<PaymentEvent> Events { get; set; } = new();
}

public class PaymentEvent
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public PaymentEventType EventType { get; set; }
    public string EventData { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Payment Payment { get; set; } = default!;
}

public class PaymentMetadata
{
    public string? CardLast4 { get; set; }
    public string? CardBrand { get; set; }
    public string? ProcessorResponse { get; set; }
    public Dictionary<string, string> AdditionalData { get; set; } = new();
}

public enum PaymentMethod
{
    CreditCard,
    DebitCard,
    PayPal,
    ApplePay,
    GooglePay,
    BankTransfer,
    Cash
}

public enum PaymentStatus
{
    Pending,
    Processing,
    Succeeded,
    Failed,
    Cancelled,
    Refunded,
    PartiallyRefunded
}

public enum PaymentEventType
{
    Created,
    Processing,
    Succeeded,
    Failed,
    Cancelled,
    Refunded,
    PartiallyRefunded
} 