using KSE.DistributedSystems.NotificationService.Models;
using KSE.DistributedSystems.NotificationService.Services;
using MassTransit;

namespace KSE.DistributedSystems.NotificationService.Consumers;

public class OrderDeliveredConsumer(IEmailService emailService, ILogger<OrderDeliveredConsumer> logger) : IConsumer<OrderDelivered>
{
    public async Task Consume(ConsumeContext<OrderDelivered> context)
    {
        var message = context.Message;

        logger.LogInformation($"Processing OrderDelivered message for Order {message.OrderId}, Customer {message.Email}");

        try
        {
            await emailService.SendEmailAsync(message.Email, "Order Delivered", $"Your order {message.OrderId} has been successfully delivered. Thank you for choosing our service!");

            logger.LogInformation($"Successfully sent order delivered notification for Order {message.OrderId} to {message.Email}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to send order delivered notification for Order {message.OrderId} to {message.Email}");
            throw;
        }
    }
}