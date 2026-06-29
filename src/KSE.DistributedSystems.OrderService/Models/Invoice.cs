namespace KSE.DistributedSystems.OrderService.Models;

public class Invoice
{
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid RestaurantId { get; set; }
    public float TotalPrice { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public DateTime CreateAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow + TimeSpan.FromMinutes(10);
    public PaymentStatus PaymentStatus { get; set; }
}