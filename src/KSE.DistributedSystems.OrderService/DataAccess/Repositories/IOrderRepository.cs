using KSE.DistributedSystems.OrderService.Models;

namespace KSE.DistributedSystems.OrderService.DataAccess.Repositories;

public interface IOrderRepository
{
    Task<Order?> GetOrderByIdAsync(Guid id);
    Task<bool> ExistsAsync(Guid id);
    Task<Order> CreateOrderAsync(Order order);

    Task<Order> UpdateOrderStatusAsync(Guid orderId, PaymentStatus status);

    Task<Order> UpdateOrderAsync(Order order);
}