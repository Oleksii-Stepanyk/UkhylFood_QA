using KSE.DistributedSystems.NotificationService.Models;
using KSE.DistributedSystems.NotificationService.Services;
using MassTransit;

namespace KSE.DistributedSystems.NotificationService.Consumers;

public class PaymentFailedConsumer(IEmailService emailService, ILogger<PaymentFailedConsumer> logger) : IConsumer<PaymentFailed>
{
    public async Task Consume(ConsumeContext<PaymentFailed> context)
    {
        var message = context.Message;

        logger.LogWarning($"Processing PaymentFailed message for Payment {message.PaymentId}, Customer {message.Email}");

        try
        {
            await emailService.SendEmailAsync(message.Email, "Payment Failed", $"Unfortunately, your payment {message.PaymentId} could not be processed. Please check your payment details and try again.");

            logger.LogInformation($"Successfully sent payment failed notification for Payment {message.PaymentId} to {message.Email}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to send payment failed notification for Payment {message.PaymentId} to {message.Email}");
            throw;
        }
    }
}