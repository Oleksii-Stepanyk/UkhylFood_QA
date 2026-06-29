namespace KSE.DistributedSystems.OrderService.DTOs;

public class PaymentFailed(Guid orderId)
{
    public Guid OrderId { get; set; } = orderId;
    public string Status { get; set; } = "Failed";
    public string? Details { get; set; }
}