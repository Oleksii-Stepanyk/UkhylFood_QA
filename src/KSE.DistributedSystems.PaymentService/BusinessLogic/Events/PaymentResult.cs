namespace KSE.DistributedSystems.OrderService.Models;

public class PaymentResult
{
    public Guid OrderId { get; set; }
    public int Status { get; set; }

    public PaymentResult() { }

    public PaymentResult(Guid orderId, int status)
    {
        OrderId = orderId;
        Status = status;
    }
}
