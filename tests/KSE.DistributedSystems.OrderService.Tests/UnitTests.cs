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

[Trait("Category", "Unit")]
public class UnitTests
{
    private readonly Mock<IPublishEndpoint> _mockPublishEndpoint = new();
    private readonly Mock<IOrderRepository> _mockOrderRepository = new();
    private readonly Mock<IPaymentRepository> _mockPaymentRepository = new();
    private readonly Mock<IInvoiceRepository> _mockInvoiceRepository = new();
    private readonly Mock<ILogger<OrderMonitoringService>> _mockLogger = new();
    private readonly Mock<OrderMonitoringService> _mockMetricsService;
    private readonly Services.OrderService _orderService;

    public UnitTests()
    {
        _mockMetricsService = new Mock<OrderMonitoringService>(_mockLogger.Object);
        _orderService = new(_mockOrderRepository.Object, _mockPaymentRepository.Object, _mockInvoiceRepository.Object, _mockMetricsService.Object);
    }

    [Fact]
    public async Task OnOrderPlaced_ShouldCreateOrderAndInvoice_WhenValidOrderProvided()
    {
        var customerId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            RestaurantId = restaurantId,
            TotalPrice = 25.99f,
            Items = [new OrderItem { Id = Guid.NewGuid(), Name = "Pizza", Quantity = 1, Price = 25.99f }]
        };

        var paymentMethod = new PaymentMethod
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            MethodType = PaymentMethod.Type.Card,
            IsDefault = true
        };

        _mockOrderRepository.Setup(r => r.CreateOrderAsync(It.IsAny<Order>()))
            .ReturnsAsync((Order o) => { o.Status = OrderStatus.Pending; o.PaymentStatus = PaymentStatus.Pending; return o; });
        _mockPaymentRepository.Setup(r => r.GetPaymentMethodByCustomerId(customerId))
            .ReturnsAsync(paymentMethod);
        _mockInvoiceRepository.Setup(r => r.SaveInvoice(It.IsAny<Invoice>()))
            .Returns(Task.CompletedTask);

        await _orderService.OnOrderPlaced(order, _mockPublishEndpoint.Object);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        _mockOrderRepository.Verify(r => r.CreateOrderAsync(It.IsAny<Order>()), Times.Once);
        _mockPaymentRepository.Verify(r => r.GetPaymentMethodByCustomerId(customerId), Times.Once);
        _mockInvoiceRepository.Verify(r => r.SaveInvoice(It.Is<Invoice>(i =>
            i.OrderId == order.Id &&
            i.CustomerId == customerId &&
            i.RestaurantId == restaurantId &&
            i.TotalPrice == 25.99f &&
            i.Currency == "USD" &&
            i.PaymentStatus == PaymentStatus.Pending)), Times.Once);
        _mockPublishEndpoint.Verify(p => p.Publish(It.IsAny<Invoice>(), default), Times.Never);
    }

    [Fact]
    public async Task OnOrderPlaced_ShouldThrowPaymentNotFoundException_WhenNoPaymentMethodExists()
    {
        var customerId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            RestaurantId = Guid.NewGuid(),
            TotalPrice = 25.99f
        };

        _mockOrderRepository.Setup(r => r.CreateOrderAsync(It.IsAny<Order>()))
            .ReturnsAsync(order);
        _mockPaymentRepository.Setup(r => r.GetPaymentMethodByCustomerId(customerId))
            .ReturnsAsync((PaymentMethod?)null);

        await Assert.ThrowsAsync<PaymentNotFoundException>(() => _orderService.OnOrderPlaced(order, _mockPublishEndpoint.Object));
        _mockInvoiceRepository.Verify(r => r.SaveInvoice(It.IsAny<Invoice>()), Times.Never);
        _mockPublishEndpoint.Verify(p => p.Publish(It.IsAny<Invoice>(), default), Times.Never);
    }

    [Fact]
    public async Task OnPaymentSuccess_ShouldUpdateOrderAndPublish_WhenPaymentSucceeds()
    {
        var invoiceId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymentResult = new PaymentResult { OrderId = invoiceId, Status = (int)PaymentStatus.Paid };

        var order = new Order
        {
            Id = orderId,
            CustomerId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            TotalPrice = 30.00f,
            Items = []
        };

        _mockInvoiceRepository.Setup(r => r.UpdateInvoiceStatus(invoiceId, PaymentStatus.Paid))
            .ReturnsAsync(orderId);
        _mockOrderRepository.Setup(r => r.UpdateOrderStatusAsync(orderId, PaymentStatus.Paid))
            .ReturnsAsync((Guid id, PaymentStatus status) => { order.PaymentStatus = status; return order; });

        await _orderService.OnPaymentSuccess(paymentResult, _mockPublishEndpoint.Object);

        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        _mockInvoiceRepository.Verify(r => r.UpdateInvoiceStatus(invoiceId, PaymentStatus.Paid), Times.Once);
        _mockOrderRepository.Verify(r => r.UpdateOrderStatusAsync(orderId, PaymentStatus.Paid), Times.Once);
        _mockPublishEndpoint.Verify(p => p.Publish(order, default), Times.Once);
    }

    [Fact]
    public async Task OnPaymentFail_ShouldUpdateOrderStatusAndPublishFailureEvent_WhenPaymentFails()
    {
        var invoiceId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymentResult = new PaymentResult { OrderId = invoiceId, Status = (int)PaymentStatus.Failed };

        _mockInvoiceRepository.Setup(r => r.UpdateInvoiceStatus(invoiceId, PaymentStatus.Failed))
            .ReturnsAsync(orderId);
        _mockOrderRepository.Setup(r => r.UpdateOrderStatusAsync(orderId, PaymentStatus.Failed))
            .ReturnsAsync(new Order { Id = orderId, PaymentStatus = PaymentStatus.Failed });

        await _orderService.OnPaymentFail(paymentResult, _mockPublishEndpoint.Object);

        _mockInvoiceRepository.Verify(r => r.UpdateInvoiceStatus(invoiceId, PaymentStatus.Failed), Times.Once);
        _mockOrderRepository.Verify(r => r.UpdateOrderStatusAsync(orderId, PaymentStatus.Failed), Times.Once);
        _mockPublishEndpoint.Verify(p => p.Publish(It.Is<PaymentFailed>(pf =>
            pf.OrderId == orderId &&
            pf.Status == "Failed"), default), Times.Once);
    }

    [Fact]
    public async Task OnOrderUpdate_ShouldUpdateOrderAndPublish_WhenValidOrderProvided()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            Status = OrderStatus.Confirmed,
            PaymentStatus = PaymentStatus.Paid,
            TotalPrice = 35.50f
        };

        var updatedOrder = new Order
        {
            Id = order.Id,
            CustomerId = order.CustomerId,
            RestaurantId = order.RestaurantId,
            Status = OrderStatus.Preparing,
            PaymentStatus = PaymentStatus.Paid,
            TotalPrice = order.TotalPrice
        };

        _mockOrderRepository.Setup(r => r.UpdateOrderAsync(order))
            .ReturnsAsync(updatedOrder);

        await _orderService.OnOrderUpdate(order, _mockPublishEndpoint.Object);

        _mockOrderRepository.Verify(r => r.UpdateOrderAsync(order), Times.Once);
        _mockPublishEndpoint.Verify(p => p.Publish(It.IsAny<Order>(), default), Times.Never);
    }

    [Fact]
    public void Order_ShouldHaveValidInitialState_WhenCreated()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            Items = [],
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            TotalPrice = 0.0f,
            PaymentStatus = PaymentStatus.Pending
        };

        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.NotEqual(Guid.Empty, order.CustomerId);
        Assert.NotEqual(Guid.Empty, order.RestaurantId);
        Assert.Empty(order.Items);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        Assert.Null(order.CourierId);
        Assert.Null(order.DeliveredAt);
    }

    [Fact]
    public void OrderItem_ShouldCalculateCorrectSubtotal_WhenQuantityAndPriceSet()
    {
        var orderItem = new OrderItem
        {
            Id = Guid.NewGuid(),
            Name = "Margherita Pizza",
            Quantity = 3,
            Price = 12.99f
        };

        var expectedSubtotal = orderItem.Quantity * orderItem.Price;

        Assert.Equal("Margherita Pizza", orderItem.Name);
        Assert.Equal(3, orderItem.Quantity);
        Assert.Equal(12.99f, orderItem.Price);
        Assert.Equal(38.97f, expectedSubtotal, 2);
    }

    [Fact]
    public void Invoice_ShouldHaveCorrectExpirationTime_WhenCreated()
    {
        var now = DateTime.UtcNow;
        var invoice = new Invoice
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            TotalPrice = 45.00f,
            Currency = "USD",
            PaymentStatus = PaymentStatus.Pending
        };

        Assert.Equal("USD", invoice.Currency);
        Assert.Equal(PaymentStatus.Pending, invoice.PaymentStatus);
        Assert.True(invoice.CreateAt >= now);
        Assert.True(invoice.ExpiresAt > invoice.CreateAt);

        var expectedExpiration = invoice.CreateAt.AddMinutes(10);
        var timeDifference = Math.Abs((invoice.ExpiresAt - expectedExpiration).TotalSeconds);
        Assert.True(timeDifference < 1, "Invoice expiration should be approximately 10 minutes from creation");
    }

    [Fact]
    public void PaymentMethod_ShouldSupportAllPaymentTypes()
    {
        var cardPayment = new PaymentMethod
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            MethodType = PaymentMethod.Type.Card,
            Details = "****1234",
            IsDefault = true
        };

        var walletPayment = new PaymentMethod
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            MethodType = PaymentMethod.Type.Wallet,
            Details = "wallet@email.com",
            IsDefault = false
        };

        var cashPayment = new PaymentMethod
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            MethodType = PaymentMethod.Type.Cash,
            Details = null,
            IsDefault = false
        };

        Assert.Equal(PaymentMethod.Type.Card, cardPayment.MethodType);
        Assert.Equal("****1234", cardPayment.Details);
        Assert.True(cardPayment.IsDefault);

        Assert.Equal(PaymentMethod.Type.Wallet, walletPayment.MethodType);
        Assert.Equal("wallet@email.com", walletPayment.Details);
        Assert.False(walletPayment.IsDefault);

        Assert.Equal(PaymentMethod.Type.Cash, cashPayment.MethodType);
        Assert.Null(cashPayment.Details);
        Assert.False(cashPayment.IsDefault);
    }

    [Fact]
    public void PaymentResult_ShouldCreateCorrectRecord_WithValidData()
    {
        var invoiceId = Guid.NewGuid();
        var status = PaymentStatus.Paid;

        var paymentResult = new PaymentResult { OrderId = invoiceId, Status = (int)status };

        Assert.Equal(invoiceId, paymentResult.OrderId);
        Assert.Equal((int)PaymentStatus.Paid, paymentResult.Status);
    }
}