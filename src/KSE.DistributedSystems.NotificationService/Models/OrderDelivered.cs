namespace KSE.DistributedSystems.NotificationService.Models;

public class OrderDelivered
{
    public string Email { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
}