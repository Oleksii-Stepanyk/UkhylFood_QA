namespace KSE.DistributedSystems.OrderService.Models;

public class OrderItem
{
    public Guid Id { get; set; }
    public int Quantity { get; set; }
    public float Price { get; set; }
    public string Name { get; set; } = null!;

    public List<Order> Orders { get; set; } = [];
}