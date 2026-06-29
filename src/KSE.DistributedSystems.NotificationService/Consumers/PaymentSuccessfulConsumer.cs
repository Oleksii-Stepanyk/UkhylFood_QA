using KSE.DistributedSystems.NotificationService.Models;
using KSE.DistributedSystems.NotificationService.Services;
using MassTransit;

namespace KSE.DistributedSystems.NotificationService.Consumers;

public class PaymentSuccessfulConsumer(IEmailService emailService, ILogger<PaymentSuccessfulConsumer> logger) : IConsumer<PaymentSuccessful>
{
    public async Task Consume(ConsumeContext<PaymentSuccessful> context)
    {
        var message = context.Message;

        logger.LogInformation($"Processing PaymentSuccessful message for Payment {message.PaymentId}, Customer {message.Email}");

        try
        {
            await emailService.SendEmailAsync(message.Email, "Payment Successful", $"Your payment {message.PaymentId} was processed successfully. Thank you for your payment!");

            logger.LogInformation($"Successfully sent payment successful notification for Payment {message.PaymentId} to {message.Email}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to send payment successful notification for Payment {message.PaymentId} to {message.Email}");
            throw;
        }
    }
}