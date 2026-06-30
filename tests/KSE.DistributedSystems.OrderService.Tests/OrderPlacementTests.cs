using KSE.DistributedSystems.OrderService.Exceptions;
using KSE.DistributedSystems.OrderService.Models;
using Moq;
using Xunit;

namespace KSE.DistributedSystems.OrderService.Tests;

public class OrderPlacementTests : OrderServiceTestBase
{
    [Fact]
    public async Task OnOrderPlaced_ShouldThrowPaymentNotFoundException_WhenNoPaymentMethodExists()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid()
        };
        MockOrderRepository.Setup(repository =>
            repository.CreateOrderAsync(It.IsAny<Order>())).ReturnsAsync(order);
        MockPaymentRepository.Setup(repository =>
            repository.GetPaymentMethodByCustomerId(order.CustomerId)).ReturnsAsync((PaymentMethod?)null);

        await Assert.ThrowsAsync<PaymentNotFoundException>(() =>
            OrderService.OnOrderPlaced(order, MockPublishEndpoint.Object));
        MockInvoiceRepository.Verify(repository =>
            repository.SaveInvoice(It.IsAny<Invoice>()), Times.Never);
    }

    [Fact]
    public async Task OnOrderPlaced_ShouldSetStatusesToPending()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Paid
        };
        var paymentMethod = new PaymentMethod
        {
            Id = Guid.NewGuid(),
            CustomerId = order.CustomerId
        };

        MockOrderRepository.Setup(repository =>
            repository.CreateOrderAsync(It.IsAny<Order>())).ReturnsAsync(order);
        MockPaymentRepository.Setup(repository =>
            repository.GetPaymentMethodByCustomerId(order.CustomerId)).ReturnsAsync(paymentMethod);

        await OrderService.OnOrderPlaced(order, MockPublishEndpoint.Object);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);

        MockOrderRepository.Verify(repository =>
            repository.CreateOrderAsync(order), Times.Once);
        MockInvoiceRepository.Verify(repository =>
            repository.SaveInvoice(It.IsAny<Invoice>()), Times.Once);
    }

    [Fact]
    public async Task OnOrderPlaced_ShouldSaveInvoice_WhenSuccessful()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            TotalPrice = 100
        };
        var paymentMethod = new PaymentMethod
        {
            Id = Guid.NewGuid(),
            CustomerId = order.CustomerId
        };

        MockOrderRepository.Setup(repository =>
            repository.CreateOrderAsync(It.IsAny<Order>())).ReturnsAsync(order);
        MockPaymentRepository.Setup(repository =>
            repository.GetPaymentMethodByCustomerId(order.CustomerId)).ReturnsAsync(paymentMethod);

        await OrderService.OnOrderPlaced(order, MockPublishEndpoint.Object);

        MockInvoiceRepository.Verify(repository =>
            repository.SaveInvoice(It.Is<Invoice>(invoice =>
                invoice.OrderId == order.Id &&
                invoice.CustomerId == order.CustomerId &&
                invoice.TotalPrice == order.TotalPrice &&
                invoice.PaymentStatus == PaymentStatus.Pending)), Times.Once);
    }
}
