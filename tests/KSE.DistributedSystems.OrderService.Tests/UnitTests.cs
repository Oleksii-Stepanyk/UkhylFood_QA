using KSE.DistributedSystems.OrderService.DataAccess.Repositories;
using KSE.DistributedSystems.OrderService.DTOs;
using KSE.DistributedSystems.OrderService.Exceptions;
using KSE.DistributedSystems.OrderService.Models;
using KSE.DistributedSystems.OrderService.Services;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace KSE.DistributedSystems.OrderService.Tests;

public abstract class OrderServiceTestBase
{
    protected readonly Mock<IPublishEndpoint> MockPublishEndpoint = new();
    protected readonly Mock<IOrderRepository> MockOrderRepository = new();
    protected readonly Mock<IPaymentRepository> MockPaymentRepository = new();
    protected readonly Mock<IInvoiceRepository> MockInvoiceRepository = new();
    protected readonly Mock<ILogger<OrderMonitoringService>> MockLogger = new();
    protected readonly OrderMonitoringService MetricsService;
    protected readonly Services.OrderService OrderService;

    protected OrderServiceTestBase()
    {
        MetricsService = new OrderMonitoringService(MockLogger.Object);
        OrderService = new Services.OrderService(
            MockOrderRepository.Object,
            MockPaymentRepository.Object,
            MockInvoiceRepository.Object,
            MetricsService);
    }
}

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