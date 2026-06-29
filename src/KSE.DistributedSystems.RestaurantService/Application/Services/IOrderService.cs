using System;
using System.Threading.Tasks;
using KSE.DistributedSystems.RestaurantService.API.DTOs;
using KSE.DistributedSystems.RestaurantService.DataAccess.Models;

namespace KSE.DistributedSystems.RestaurantService.Application.Services;

public interface IOrderService
{
    Task<OrderResponse> GetOrderByIdAsync(Guid id);
    Task<OrderResponse> UpdateOrderStatusAsync(UpdateOrderStatusRequest request);
    Task CreateOrderFromQueueAsync(Order order);
    Task UpdateOrderStatusFromQueueAsync(Order order);
    Task<PaginatedResult<OrderResponse>> GetOrdersByRestaurantId(Guid restaurantId, int offset, int limit);
}