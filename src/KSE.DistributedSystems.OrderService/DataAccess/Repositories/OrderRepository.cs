using System.Transactions;
using KSE.DistributedSystems.OrderService.Models;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;

namespace KSE.DistributedSystems.OrderService.DataAccess.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly OrderDbContext _db;

    public OrderRepository(OrderDbContext db)
    {
        _db = db;
    }


    public async Task<Order?> GetOrderByIdAsync(Guid id) =>
        await _db.Orders.FindAsync(id);

    public async Task<bool> ExistsAsync(Guid id) =>
        await _db.Orders.AnyAsync(o => o.Id == id);

    public async Task<Order> CreateOrderAsync(Order order)
    {
        var result = await _db.Orders.AddAsync(order);
        await _db.SaveChangesAsync();

        return result.Entity;
    }

    public async Task<Order> UpdateOrderStatusAsync(Guid orderId, PaymentStatus status)
    {
        var order = await GetOrderByIdAsync(orderId);
        if (order == null)
        {
            // HADNLE
        }
        
        order!.PaymentStatus = status;
        var updated = _db.Orders.Update(order);
        await _db.SaveChangesAsync();
        
        return updated.Entity;
    }

    public async Task<Order> UpdateOrderAsync(Order order)
    {
        var existingOrder = await _db.Orders.FindAsync(order.Id);
        if (existingOrder != null)
        {
            existingOrder.Status = order.Status;
            _db.Orders.Update(existingOrder);
            await _db.SaveChangesAsync();
            return existingOrder;
        }

        return order;
    }
}