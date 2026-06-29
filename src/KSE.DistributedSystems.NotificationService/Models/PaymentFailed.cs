namespace KSE.DistributedSystems.NotificationService.Models;

public class PaymentFailed
{
    public string Email { get; set; } = string.Empty;
    public string PaymentId { get; set; } = string.Empty;
}