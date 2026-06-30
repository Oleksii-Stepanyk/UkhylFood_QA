using KSE.DistributedSystems.OrderService.DataAccess.Repositories;
using KSE.DistributedSystems.OrderService.DTOs;
using KSE.DistributedSystems.OrderService.Exceptions;
using KSE.DistributedSystems.OrderService.Models;
using KSE.DistributedSystems.OrderService.Services.Interfaces;
using KSE.DistributedSystems.NotificationService.Models;
using MassTransit;
using System.Diagnostics;

namespace KSE.DistributedSystems.OrderService.Services;

public class OrderService : IOrderService
{
    private readonly IPublishEndpoint _endpoint;
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IInvoiceRepository _invoices;
    private readonly OrderMonitoringService _metricsService;

    public OrderService(IOrderRepository orderRepository, IPaymentRepository paymentRepository, IInvoiceRepository invoices, OrderMonitoringService metricsService)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _invoices = invoices;
        _metricsService = metricsService;
    }

    public async Task OnOrderPlaced(Order order, IPublishEndpoint publishEndpoint)
    {
        var stopwatch = Stopwatch.StartNew();
        var success = false;

        try
        {
            Console.WriteLine($"[OnOrderPlaced] Starting for order {order.Id}");
            order.Status = OrderStatus.Pending;
            order.PaymentStatus = PaymentStatus.Pending;

            Console.WriteLine($"[OnOrderPlaced] Calling CreateOrderAsync...");
            var created = await _orderRepository.CreateOrderAsync(order);
            Console.WriteLine($"[OnOrderPlaced] Order created in repository. Fetching payment method for {created.CustomerId}...");

            var paymentMethod = await _paymentRepository.GetPaymentMethodByCustomerId(created.CustomerId)
                ?? throw new PaymentNotFoundException();
            Console.WriteLine($"[OnOrderPlaced] Payment method found. Saving invoice...");

            var invoice = new Invoice
            {
                OrderId = created.Id,
                CustomerId = created.CustomerId,
                RestaurantId = created.RestaurantId,
                TotalPrice = created.TotalPrice,
                PaymentMethod = paymentMethod,
                Currency = "USD",
                PaymentStatus = PaymentStatus.Pending
            };
            await _invoices.SaveInvoice(invoice);
            Console.WriteLine($"[OnOrderPlaced] Invoice saved. Publishing OrderCreated event...");

            await publishEndpoint.Publish(new OrderCreated
            {
                Email = paymentMethod.Details ?? "customer@example.com",
                OrderId = created.Id.ToString()
            });
            Console.WriteLine($"[OnOrderPlaced] OrderCreated event published.");

            success = true;
            _metricsService.IncrementOrderProcessed("order_placed", order.Status.ToString(), order.PaymentStatus.ToString());
        }
        finally
        {
            stopwatch.Stop();
            _metricsService.RecordOrderProcessingDuration("order_placed", stopwatch.ElapsedMilliseconds, success);
        }
    }

    public async Task OnPaymentSuccess(PaymentResult result, IPublishEndpoint publishEndpoint)
    {
        var stopwatch = Stopwatch.StartNew();
        var success = false;

        try
        {
            var orderId = await _invoices.UpdateInvoiceStatus(result.OrderId, (PaymentStatus)result.Status);

            var order = await _orderRepository.UpdateOrderStatusAsync(orderId, PaymentStatus.Paid);

            order.Items.ForEach(i => i.Orders = []);
            await publishEndpoint.Publish(order);

            success = true;
            _metricsService.IncrementOrderProcessed("payment_success", order.Status.ToString(), order.PaymentStatus.ToString());
        }
        finally
        {
            stopwatch.Stop();
            _metricsService.RecordOrderProcessingDuration("payment_success", stopwatch.ElapsedMilliseconds, success);
        }
    }

    public async Task OnPaymentFail(PaymentResult result, IPublishEndpoint publishEndpoint)
    {
        var stopwatch = Stopwatch.StartNew();
        var success = false;

        try
        {
            var orderId = await _invoices.UpdateInvoiceStatus(result.OrderId, (PaymentStatus)result.Status);

            await _orderRepository.UpdateOrderStatusAsync(orderId, PaymentStatus.Failed);

            var failedPayment = new DTOs.PaymentFailed(orderId);
            await publishEndpoint.Publish(failedPayment);

            success = true;
            _metricsService.IncrementOrderProcessed("payment_failed", "unknown", PaymentStatus.Failed.ToString());
        }
        finally
        {
            stopwatch.Stop();
            _metricsService.RecordOrderProcessingDuration("payment_failed", stopwatch.ElapsedMilliseconds, success);
        }
    }

    public async Task OnOrderUpdate(Order updateOrder, IPublishEndpoint publishEndpoint)
    {
        var stopwatch = Stopwatch.StartNew();
        var success = false;

        try
        {
            var updated = await _orderRepository.UpdateOrderAsync(updateOrder);
            success = true;
            _metricsService.IncrementOrderProcessed("order_update", updated.Status.ToString(), updated.PaymentStatus.ToString());
        }
        finally
        {
            stopwatch.Stop();
            _metricsService.RecordOrderProcessingDuration("order_update", stopwatch.ElapsedMilliseconds, success);
        }
    }
}