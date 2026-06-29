using KSE.DistributedSystems.OrderService.Models;
using KSE.DistributedSystems.OrderService.Services;
using KSE.DistributedSystems.OrderService.Services.Interfaces;
using MassTransit;
using System.Diagnostics;

namespace KSE.DistributedSystems.OrderService.Consumers;

public class PaymentResultConsumer(ILogger<PaymentResultConsumer> logger, IOrderService orderService, OrderMonitoringService metricsService) : IConsumer<PaymentResult>
{
    public async Task Consume(ConsumeContext<PaymentResult> context)
    {
        var id = Guid.NewGuid();
        var stopwatch = Stopwatch.StartNew();
        var success = false;

        try
        {
            var paymentResult = context.Message;
            logger.LogInformation("[{Id}] Received raw payment response for OrderId {OrderId} with Status {Status}", id, paymentResult?.OrderId, paymentResult?.Status);
            if (paymentResult == null) {
                return;
            }

            switch ((PaymentStatus)paymentResult.Status)
            {
                case PaymentStatus.Failed:
                    await orderService.OnPaymentFail(paymentResult, context);
                    success = true;
                    metricsService.IncrementOrderProcessed("payment_message_consumed", "payment_failed");
                    break;
                case PaymentStatus.Paid:
                    await orderService.OnPaymentSuccess(paymentResult, context);
                    success = true;
                    metricsService.IncrementOrderProcessed("payment_message_consumed", "payment_success");
                    break;
                case PaymentStatus.Pending:
                default:
                    logger.LogInformation("[{Id}] Received payment response {Status} for order {OrderId}",
                id,
                paymentResult.Status,
                paymentResult.OrderId);
                    metricsService.IncrementOrderProcessed("payment_message_consumed", "payment_pending");
                    break;
            }
        }
        catch (Exception e)
        {
            metricsService.IncrementOrderProcessed("payment_message_consumed", "error");
            
            logger.LogError(e,
                "[{Id}] Error processing response at {Timestamp} from Payment service. Exception: {ExceptionMessage}",
                id,
                DateTime.UtcNow.ToString("o"),
                e.Message);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            metricsService.RecordOrderProcessingDuration("payment_message_consumed", stopwatch.ElapsedMilliseconds, success);
        }
    }
}