using System;
using KSE.DistributedSystems.RestaurantService.DataAccess.Models;

namespace KSE.DistributedSystems.RestaurantService.API.DTOs;

public class UpdateOrderStatusRequest
{
    public Guid OrderId { get; set; }
    public OrderStatus OrderStatus { get; set; }
}