using KSE.DistributedSystems.OrderService.DTOs;
using KSE.DistributedSystems.OrderService.Models;
using Moq;
using Xunit;

namespace KSE.DistributedSystems.OrderService.Tests;

public class PaymentFailTests : OrderServiceTestBase
{
    [Fact]
    public async Task OnPaymentFail_ShouldUpdateInvoiceStatusToFailed()
    {
        var orderId = Guid.NewGuid();
        var result = new PaymentResult
        {
            OrderId = Guid.NewGuid(),
            Status = (int)PaymentStatus.Failed
        };

        MockInvoiceRepository.Setup(repository =>
            repository.UpdateInvoiceStatus(result.OrderId, PaymentStatus.Failed)).ReturnsAsync(orderId);
        MockOrderRepository.Setup(repository =>
            repository.UpdateOrderStatusAsync(orderId, PaymentStatus.Failed)).ReturnsAsync(new Order());

        await OrderService.OnPaymentFail(result, MockPublishEndpoint.Object);

        MockInvoiceRepository.Verify(repository =>
            repository.UpdateInvoiceStatus(result.OrderId, PaymentStatus.Failed), Times.Once);
    }

    [Fact]
    public async Task OnPaymentFail_ShouldUpdateOrderStatusToFailed()
    {
        var orderId = Guid.NewGuid();
        var result = new PaymentResult
        {
            OrderId = Guid.NewGuid(),
            Status = (int)PaymentStatus.Failed
        };

        MockInvoiceRepository.Setup(repository =>
            repository.UpdateInvoiceStatus(result.OrderId, PaymentStatus.Failed)).ReturnsAsync(orderId);
        MockOrderRepository.Setup(repository =>
            repository.UpdateOrderStatusAsync(orderId, PaymentStatus.Failed)).ReturnsAsync(new Order());

        await OrderService.OnPaymentFail(result, MockPublishEndpoint.Object);

        MockOrderRepository.Verify(repository =>
            repository.UpdateOrderStatusAsync(orderId, PaymentStatus.Failed), Times.Once);
    }

    [Fact]
    public async Task OnPaymentFail_ShouldPublishPaymentFailedEvent()
    {
        var orderId = Guid.NewGuid();
        var result = new PaymentResult
        {
            OrderId = Guid.NewGuid(),
            Status = (int)PaymentStatus.Failed
        };

        MockInvoiceRepository.Setup(repository =>
            repository.UpdateInvoiceStatus(result.OrderId, PaymentStatus.Failed)).ReturnsAsync(orderId);
        MockOrderRepository.Setup(repository =>
            repository.UpdateOrderStatusAsync(orderId, PaymentStatus.Failed)).ReturnsAsync(new Order());

        await OrderService.OnPaymentFail(result, MockPublishEndpoint.Object);

        MockPublishEndpoint.Verify(publisher =>
            publisher.Publish(It.Is<PaymentFailed>(paymentFailed =>
                paymentFailed.OrderId == orderId && paymentFailed.Status == "Failed"), default), Times.Once);
    }
}
