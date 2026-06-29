using KSE.DistributedSystems.CustomerService.Services;
using KSE.DistributedSystems.OrderService.Models;
using MassTransit;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

using LogContext = Serilog.Context.LogContext;


namespace KSE.DistributedSystems.CustomerService.Consumers;

public class OrderConsumer : IConsumer<Order>
{
    private readonly ICustomerService _customerService;
    private readonly ILogger<OrderConsumer> _logger;

    public OrderConsumer(
        ICustomerService customerService,
        ILogger<OrderConsumer> logger)
    {
        _customerService = customerService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<Order> context)
    {
        using var _ = LogContext.PushProperty("MessageId", context.MessageId);
        
        var order = context.Message;
        
        _logger.LogInformation("Processing order {OrderId} for customer {CustomerId}", 
            order.Id, order.CustomerId);

        if (order.CustomerId == Guid.Empty)
        {
            _logger.LogCritical("Customer id is equal to null");
            throw new ArgumentException("Order does not have a valid CustomerId");
        }

        var customer = await _customerService.GetCustomerAsync(order.CustomerId);
        if (customer == null)
        {
            _logger.LogCritical("Customer is equal to null");
            throw new InvalidOperationException($"Customer {order.CustomerId} not found for order {order.Id}");
        }

        var pointsToAdd = (uint)Math.Floor(order.TotalPrice / 10);
        var newPoints = customer.LoyaltyPoints + pointsToAdd;
        var updatedCustomer = customer with { LoyaltyPoints = newPoints };
        
        await _customerService.UpdateCustomerAsync(customer.Id, updatedCustomer);

        // Publish events using MassTransit's native outbox pattern
        await context.Publish(new CustomerLoyaltyPointsUpdated
        {
            CustomerId = customer.Id,
            OrderId = order.Id,
            PointsAdded = pointsToAdd,
            NewTotalPoints = newPoints,
            UpdatedAt = DateTime.UtcNow
        });

        _logger.LogInformation("Successfully processed order {OrderId}, added {Points} loyalty points to customer {CustomerId}", 
            order.Id, pointsToAdd, customer.Id);
    }
}

// Example event for downstream processing
public record CustomerLoyaltyPointsUpdated
{
    public Guid CustomerId { get; init; }
    public Guid OrderId { get; init; }
    public uint PointsAdded { get; init; }
    public uint NewTotalPoints { get; init; }
    public DateTime UpdatedAt { get; init; }
} 