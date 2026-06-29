using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KSE.DistributedSystems.RestaurantService.Application.Exceptions;
using KSE.DistributedSystems.RestaurantService.DataAccess.Models;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace KSE.DistributedSystems.RestaurantService.DataAccess.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly RestaurantDbContext _db;

    public OrderRepository(RestaurantDbContext db)
    {
        _db = db;
    }

    public async Task<(ICollection<Order>, int)> GetOrdersByRestaurantId(Guid id, int offset, int limit)
    {
        var orders = _db.Orders.AsNoTracking();
        var totalCount = await orders.CountAsync();
        
        orders = orders.Skip(offset).Take(limit);

        return (await orders.ToListAsync(), totalCount);
    }

    public async Task<Order?> GetOrderByIdAsync(Guid id)
        => await _db.Orders.FindAsync(id);

    public async Task<Order> CreateOrderAsync(Order order)
    {
        foreach (var item in order.Items)
        {
            if (item.Id == Guid.Empty)
            {
                item.Id = Guid.NewGuid();
            }
        }
        await _db.Orders.AddAsync(order);
        await _db.SaveChangesAsync();
        return order;
    }

    public async Task<Order> UpdateOrderAsync(Guid orderId, OrderStatus status)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var order = await GetOrderByIdAsync(orderId);
        if (order == null)
            throw new OrderNotFoundException(orderId);
        
        order.Status = status;
        _db.Orders.Update(order);
        await _db.SaveChangesAsync();

        await tx.CommitAsync();
        return order;
    }
}