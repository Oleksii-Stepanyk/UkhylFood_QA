using KSE.DistributedSystems.NotificationService.Models;
using KSE.DistributedSystems.NotificationService.Services;
using MassTransit;

namespace KSE.DistributedSystems.NotificationService.Consumers;

public class OrderCreatedConsumer(IEmailService emailService, ILogger<OrderCreatedConsumer> logger) : IConsumer<OrderCreated>
{
    public async Task Consume(ConsumeContext<OrderCreated> context)
    {
        var message = context.Message;

        logger.LogInformation($"Processing OrderCreated message for Order {message.OrderId}, Customer {message.Email}");

        try
        {
            await emailService.SendEmailAsync(message.Email, "Order Created", $"Your order {message.OrderId} has been created and is being processed.");

            logger.LogInformation($"Successfully sent order created notification for Order {message.OrderId} to {message.Email}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to send order created notification for Order {message.OrderId} to {message.Email}");
            throw;
        }
    }
}