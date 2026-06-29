namespace KSE.DistributedSystems.OrderService.Models;

public class PaymentResult
{
    public Guid OrderId { get; set; }
    public int Status { get; set; }
}