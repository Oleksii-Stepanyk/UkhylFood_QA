using System;
using System.Collections.Generic;

namespace KSE.DistributedSystems.CustomerService.DataAccess.Models;

public class OrderCreateDTO
{
    public Guid CustomerId { get; set; }
    public Guid RestaurantId { get; set; }
    public List<OrderItemDTO> Items { get; set; } = new();
    public float TotalPrice { get; set; }
}

public class OrderItemDTO
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public float Price { get; set; }
}
