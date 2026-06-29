using System.ComponentModel.DataAnnotations;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;

namespace KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;

public class PaymentRequestDto
{
    [Required]
    public Guid OrderId { get; set; }
    
    [Required]
    public Guid CustomerId { get; set; }
    
    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public decimal Amount { get; set; }
    
    [Required]
    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = "USD";
    
    [Required]
    public PaymentMethod PaymentMethod { get; set; }
    
    public PaymentCardDto? CardDetails { get; set; }
    public Dictionary<string, string> AdditionalData { get; set; } = new();
}

public class PaymentCardDto
{
    [Required]
    [CreditCard]
    public string CardNumber { get; set; } = string.Empty;
    
    [Required]
    [StringLength(2, MinimumLength = 2)]
    public string ExpiryMonth { get; set; } = string.Empty;
    
    [Required]
    [StringLength(4, MinimumLength = 4)]
    public string ExpiryYear { get; set; } = string.Empty;
    
    [Required]
    [StringLength(4, MinimumLength = 3)]
    public string Cvv { get; set; } = string.Empty;
    
    [Required]
    public string CardholderName { get; set; } = string.Empty;
} 