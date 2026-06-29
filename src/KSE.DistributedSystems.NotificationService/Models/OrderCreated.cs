namespace KSE.DistributedSystems.NotificationService.Models;

public class OrderCreated
{
    public string Email { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
}