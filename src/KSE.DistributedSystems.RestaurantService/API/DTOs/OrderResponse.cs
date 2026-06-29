using System;
using System.Collections.Generic;
using KSE.DistributedSystems.RestaurantService.DataAccess.Models;

namespace KSE.DistributedSystems.RestaurantService.API.DTOs;

public class OrderResponse
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid? CourierId { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public float TotalPrice { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
}