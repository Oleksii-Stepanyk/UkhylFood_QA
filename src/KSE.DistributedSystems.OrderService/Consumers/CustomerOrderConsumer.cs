using KSE.DistributedSystems.OrderService.Models;
using KSE.DistributedSystems.OrderService.Services;
using KSE.DistributedSystems.OrderService.Services.Interfaces;
using MassTransit;
using System.Diagnostics;

using KSE.DistributedSystems.OrderService.DataAccess.Repositories;

namespace KSE.DistributedSystems.OrderService.Consumers;

public class CustomerOrderConsumer(IOrderService orderService, IOrderRepository repository, ILogger<CustomerOrderConsumer> logger, OrderMonitoringService metricsService) : IConsumer<Order>
{
    public async Task Consume(ConsumeContext<Order> context)
    {
        var id = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();
        var success = false;
        
        try
        {
            logger.LogInformation("[{Id}] Processing entity from customer.order queue at {TimeStamp}",
                id,
                DateTime.UtcNow.ToString("o"));
            
            var order = context.Message;
            Console.WriteLine($"[{id}] Order message received. Status: {order.Status}");
            
            var exists = await repository.ExistsAsync(order.Id);
            
            if (!exists && order.Status == OrderStatus.Pending)
            {
                Console.WriteLine($"[{id}] Calling OnOrderPlaced...");
                await orderService.OnOrderPlaced(order, context);
            }
            else if (exists)
            {
                Console.WriteLine($"[{id}] Calling OnOrderUpdate...");
                await orderService.OnOrderUpdate(order, context);
            }
            else
            {
                Console.WriteLine($"[{id}] Order not found and not pending. Ignoring.");
            }
            
            Console.WriteLine($"[{id}] Finished processing successfully.");
            success = true;
            metricsService.IncrementOrderProcessed("customer_message_consumed", "success");
            
            logger.LogInformation("[{Id}] Entity processed successfully from customer.order queue at {TimeStamp}",
                id,
                DateTime.UtcNow.ToString("o"));
        }
        catch (Exception e)
        {
            Console.WriteLine($"[{id}] EXCEPTION in CustomerOrderConsumer: {e}");
            metricsService.IncrementOrderProcessed("customer_message_consumed", "error");
            
            logger.LogError(e,
                "[{Id}] Error processing request at {Timestamp}. Exception: {ExceptionMessage}",
                id,
                DateTime.UtcNow.ToString("o"),
                e.Message);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            metricsService.RecordOrderProcessingDuration("customer_message_consumed", stopwatch.ElapsedMilliseconds, success);
        }
    }
}