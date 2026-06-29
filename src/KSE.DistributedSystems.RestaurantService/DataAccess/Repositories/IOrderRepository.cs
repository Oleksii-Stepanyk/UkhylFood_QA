using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using KSE.DistributedSystems.RestaurantService.DataAccess.Models;

namespace KSE.DistributedSystems.RestaurantService.DataAccess.Repositories;

public interface IOrderRepository
{
    Task<(ICollection<Order>, int)> GetOrdersByRestaurantId(Guid id, int offset, int limit);
    Task<Order?> GetOrderByIdAsync(Guid id);
    Task<Order> CreateOrderAsync(Order order);
    Task<Order> UpdateOrderAsync(Guid orderId, OrderStatus status);
}