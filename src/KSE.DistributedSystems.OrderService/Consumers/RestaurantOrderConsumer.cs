using KSE.DistributedSystems.OrderService.Models;
using KSE.DistributedSystems.OrderService.Services.Interfaces;
using MassTransit;

namespace KSE.DistributedSystems.OrderService.Consumers;

public class RestaurantOrderConsumer(IOrderService courierService, ILogger<RestaurantOrderConsumer> logger) : IConsumer<Order>
{
    public async Task Consume(ConsumeContext<Order> context)
    {
        var id = Guid.NewGuid();
        var order = context.Message;
        
        Console.WriteLine($"[RestaurantOrderConsumer] Received order update for OrderId: {order.Id}");

        try
        {
            await courierService.OnOrderUpdate(order, context);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[{id}] EXCEPTION in RestaurantOrderConsumer: {e}");
            logger.LogError(e,
                "[{Id}] Error processing request at {Timestamp} from Restaurant service. Exception: {ExceptionMessage}",
                id,
                DateTime.UtcNow.ToString("o"),
                e.Message);
        }
    }
}