using KSE.DistributedSystems.OrderService.Models;
using KSE.DistributedSystems.OrderService.Services.Interfaces;
using MassTransit;

namespace KSE.DistributedSystems.OrderService.Consumers;

public class CourierOrderConsumer(ILogger<CourierOrderConsumer> logger, IOrderService orderService) : IConsumer<Order>
{
    public async Task Consume(ConsumeContext<Order> context)
    {
        var id = Guid.NewGuid();
        var order = context.Message;

        try
        {
            await orderService.OnOrderUpdate(order, context);
        }
        catch (Exception e)
        {
            Console.WriteLine($"[{id}] EXCEPTION in CourierOrderConsumer: {e}");
            logger.LogError(e,
                "[{Id}] Error processing request at {Timestamp} from Courier service. Exception: {ExceptionMessage}",
                id,
                DateTime.UtcNow.ToString("o"),
                e.Message);
        }
    }
}