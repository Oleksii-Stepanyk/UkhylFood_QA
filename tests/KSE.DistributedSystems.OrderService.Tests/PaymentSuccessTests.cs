using KSE.DistributedSystems.OrderService.Models;
using Moq;
using Xunit;

namespace KSE.DistributedSystems.OrderService.Tests;

public class PaymentSuccessTests : OrderServiceTestBase
{
    [Fact]
    public async Task OnPaymentSuccess_ShouldUpdateInvoiceStatus()
    {
        var orderId = Guid.NewGuid();
        var result = new PaymentResult
        {
            OrderId = Guid.NewGuid(),
            Status = (int)PaymentStatus.Paid
        };
        var order = new Order
        {
            Id = orderId,
            Items = []
        };

        MockInvoiceRepository.Setup(repository =>
            repository.UpdateInvoiceStatus(result.OrderId, PaymentStatus.Paid)).ReturnsAsync(orderId);
        MockOrderRepository.Setup(repository =>
            repository.UpdateOrderStatusAsync(orderId, PaymentStatus.Paid)).ReturnsAsync(order);

        await OrderService.OnPaymentSuccess(result, MockPublishEndpoint.Object);

        MockInvoiceRepository.Verify(repository =>
            repository.UpdateInvoiceStatus(result.OrderId, PaymentStatus.Paid), Times.Once);
    }

    [Fact]
    public async Task OnPaymentSuccess_ShouldUpdateOrderStatus()
    {
        var orderId = Guid.NewGuid();
        var result = new PaymentResult
        {
            OrderId = Guid.NewGuid(),
            Status = (int)PaymentStatus.Paid
        };
        var order = new Order
        {
            Id = orderId,
            Items = []
        };

        MockInvoiceRepository.Setup(repository =>
            repository.UpdateInvoiceStatus(result.OrderId, PaymentStatus.Paid)).ReturnsAsync(orderId);
        MockOrderRepository.Setup(repository =>
            repository.UpdateOrderStatusAsync(orderId, PaymentStatus.Paid)).ReturnsAsync(order);

        await OrderService.OnPaymentSuccess(result, MockPublishEndpoint.Object);

        MockOrderRepository.Verify(repository =>
            repository.UpdateOrderStatusAsync(orderId, PaymentStatus.Paid), Times.Once);
    }

    [Fact]
    public async Task OnPaymentSuccess_ShouldPublishUpdatedOrderEvent()
    {
        var orderId = Guid.NewGuid();
        var result = new PaymentResult
        {
            OrderId = Guid.NewGuid(),
            Status = (int)PaymentStatus.Paid
        };
        var order = new Order
        {
            Id = orderId,
            Items = []
        };

        MockInvoiceRepository.Setup(repository =>
            repository.UpdateInvoiceStatus(result.OrderId, PaymentStatus.Paid)).ReturnsAsync(orderId);
        MockOrderRepository.Setup(repository =>
            repository.UpdateOrderStatusAsync(orderId, PaymentStatus.Paid)).ReturnsAsync(order);

        await OrderService.OnPaymentSuccess(result, MockPublishEndpoint.Object);

        MockPublishEndpoint.Verify(publisher =>
            publisher.Publish(order, default), Times.Once);
    }
}
