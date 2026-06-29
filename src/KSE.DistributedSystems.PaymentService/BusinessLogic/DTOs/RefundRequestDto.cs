using System.ComponentModel.DataAnnotations;

namespace KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;

public class RefundRequestDto
{
    [Required]
    public Guid PaymentId { get; set; }
    
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public decimal? Amount { get; set; }
    
    [StringLength(500)]
    public string? Reason { get; set; }
} 