using KSE.DistributedSystems.OrderService.Models;
using MassTransit;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;

namespace KSE.DistributedSystems.PaymentService.BusinessLogic.Consumers;

public class OrderConsumer : IConsumer<Order>
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<OrderConsumer> _logger;

    public OrderConsumer(IPaymentService paymentService, ILogger<OrderConsumer> logger)
    {
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Consume(ConsumeContext<Order> context)
    {
        var order = context.Message;

        if (order == null)
        {
            _logger.LogWarning("Received null order message");
            return;
        }

        _logger.LogInformation("Processing order {OrderId} with status {Status} for payment", 
            order.Id, order.Status);

        try
        {
            switch (order.Status)
            {
                case OrderStatus.Confirmed:
                    await HandleOrderConfirmed(order);
                    break;

                case OrderStatus.Cancelled:
                    await HandleOrderCancelled(order);
                    break;

                default:
                    _logger.LogDebug("Order {OrderId} status {Status} does not require payment action", 
                        order.Id, order.Status);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing order {OrderId} for payment", order.Id);
            throw;
        }
    }

    private async Task HandleOrderConfirmed(Order order)
    {
        _logger.LogInformation("Order {OrderId} confirmed, initiating payment processing", order.Id);
        
        var existingPayment = await _paymentService.GetPaymentByOrderIdAsync(order.Id);
        if (existingPayment != null)
        {
            _logger.LogInformation("Payment already exists for order {OrderId}", order.Id);
            return;
        }
        
        var paymentRequest = new PaymentRequestDto
        {
            OrderId = order.Id,
            CustomerId = order.CustomerId,
            Amount = (decimal)order.TotalPrice,
            Currency = "USD",
            PaymentMethod = DataAccess.Entities.PaymentMethod.CreditCard,
            AdditionalData = new Dictionary<string, string>
            {
                ["order_items_count"] = order.Items.Count.ToString(),
                ["restaurant_id"] = order.RestaurantId.ToString(),
                ["auto_processed"] = "true"
            }
        };

        try
        {
            var paymentResult = await _paymentService.ProcessPaymentAsync(paymentRequest);
            _logger.LogInformation("Payment {PaymentId} processed for order {OrderId} with status {Status}",
                paymentResult.Id, order.Id, paymentResult.Status);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            _logger.LogInformation("Payment already exists for order {OrderId}", order.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process payment for order {OrderId}", order.Id);
            throw;
        }
    }

    private async Task HandleOrderCancelled(Order order)
    {
        _logger.LogInformation("Order {OrderId} cancelled, checking for payment to cancel", order.Id);

        var existingPayment = await _paymentService.GetPaymentByOrderIdAsync(order.Id);
        if (existingPayment == null)
        {
            _logger.LogInformation("No payment found for cancelled order {OrderId}", order.Id);
            return;
        }
        
        if (existingPayment.Status == DataAccess.Entities.PaymentStatus.Pending || 
            existingPayment.Status == DataAccess.Entities.PaymentStatus.Processing)
        {
            try
            {
                var cancelResult = await _paymentService.CancelPaymentAsync(existingPayment.Id);
                if (cancelResult != null)
                {
                    _logger.LogInformation("Payment {PaymentId} cancelled for order {OrderId}",
                        existingPayment.Id, order.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel payment {PaymentId} for order {OrderId}",
                    existingPayment.Id, order.Id);
                throw;
            }
        }
        else if (existingPayment.Status == DataAccess.Entities.PaymentStatus.Succeeded)
        {
            _logger.LogInformation("Order {OrderId} cancelled but payment {PaymentId} already succeeded. Manual refund may be required.",
                order.Id, existingPayment.Id);
        }
        else
        {
            _logger.LogInformation("Payment {PaymentId} for order {OrderId} has status {Status}, no action needed",
                existingPayment.Id, order.Id, existingPayment.Status);
        }
    }
} 