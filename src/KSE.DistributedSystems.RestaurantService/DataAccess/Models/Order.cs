using System;
using System.Collections.Generic;

namespace KSE.DistributedSystems.RestaurantService.DataAccess.Models;

public class Order
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

public enum OrderStatus
{
    Pending,
    Confirmed,
    Preparing,
    ReadyForPickup,
    InDelivery,
    Delivered,
    Cancelled
}

public enum PaymentStatus
{
    Paid,
    Failed,
    Pending
}