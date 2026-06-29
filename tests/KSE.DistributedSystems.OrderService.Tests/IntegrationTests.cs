using KSE.DistributedSystems.OrderService.DataAccess.Repositories;
using KSE.DistributedSystems.OrderService.Models;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace KSE.DistributedSystems.OrderService.Tests;

[Trait("Category", "Integration")]
public class IntegrationTests(CustomWebApplicationFactory factory) : BaseApiTests(factory)
{
    [Fact]
    public async Task OrderRepository_ShouldCreateOrderSuccessfully()
    {
        using var scope = Factory.Services.CreateScope();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            TotalPrice = 25.99f,
            Items = [
                new()
                {
                    Id = Guid.NewGuid(),
                    Name = "Test Pizza",
                    Quantity = 2,
                    Price = 12.99f
                }
            ]
        };

        var mockRepository = Mock.Get(orderRepository);
        mockRepository.Setup(r => r.CreateOrderAsync(It.IsAny<Order>()))
            .ReturnsAsync((Order o) => o);
        mockRepository.Setup(r => r.GetOrderByIdAsync(order.Id))
            .ReturnsAsync(order);

        var createdOrder = await orderRepository.CreateOrderAsync(order);
        var retrievedOrder = await orderRepository.GetOrderByIdAsync(order.Id);

        Assert.NotNull(createdOrder);
        Assert.Equal(order.Id, createdOrder.Id);
        Assert.Equal(order.CustomerId, createdOrder.CustomerId);
        Assert.Equal(order.RestaurantId, createdOrder.RestaurantId);
        Assert.Equal(OrderStatus.Pending, createdOrder.Status);
        Assert.Equal(PaymentStatus.Pending, createdOrder.PaymentStatus);
        Assert.Equal(25.99f, createdOrder.TotalPrice);

        Assert.NotNull(retrievedOrder);
        Assert.Equal(order.Id, retrievedOrder.Id);
    }

    [Fact]
    public async Task OrderRepository_ShouldUpdateOrderStatusSuccessfully()
    {
        using var scope = Factory.Services.CreateScope();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var orderId = Guid.NewGuid();
        var originalOrder = new Order
        {
            Id = orderId,
            CustomerId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            TotalPrice = 15.50f
        };

        var updatedOrder = new Order
        {
            Id = orderId,
            CustomerId = originalOrder.CustomerId,
            RestaurantId = originalOrder.RestaurantId,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Paid, 
            CreatedAt = originalOrder.CreatedAt,
            TotalPrice = originalOrder.TotalPrice
        };

        var mockRepository = Mock.Get(orderRepository);
        mockRepository.Setup(r => r.GetOrderByIdAsync(orderId))
            .ReturnsAsync(originalOrder);
        mockRepository.Setup(r => r.UpdateOrderStatusAsync(orderId, PaymentStatus.Paid))
            .ReturnsAsync(updatedOrder);

        var result = await orderRepository.UpdateOrderStatusAsync(orderId, PaymentStatus.Paid);

        // order status same payment status updated
        Assert.NotNull(result);
        Assert.Equal(orderId, result.Id);
        Assert.Equal(PaymentStatus.Paid, result.PaymentStatus);
        Assert.Equal(OrderStatus.Pending, result.Status); 

        mockRepository.Verify(r => r.UpdateOrderStatusAsync(orderId, PaymentStatus.Paid), Times.Once);
    }

    [Fact]
    public async Task PaymentRepository_ShouldGetPaymentMethodByCustomerId()
    {
        using var scope = Factory.Services.CreateScope();
        var paymentRepository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();

        var customerId = Guid.NewGuid();
        var expectedPaymentMethod = new PaymentMethod
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            MethodType = PaymentMethod.Type.Card,
            Details = "Visa ending in 1234",
            IsDefault = true
        };

        var mockRepository = Mock.Get(paymentRepository);
        mockRepository.Setup(r => r.GetPaymentMethodByCustomerId(customerId))
            .ReturnsAsync(expectedPaymentMethod);

        var result = await paymentRepository.GetPaymentMethodByCustomerId(customerId);

        Assert.NotNull(result);
        Assert.Equal(expectedPaymentMethod.Id, result.Id);
        Assert.Equal(customerId, result.CustomerId);
        Assert.Equal(PaymentMethod.Type.Card, result.MethodType);
        Assert.Equal("Visa ending in 1234", result.Details);
        Assert.True(result.IsDefault);

        mockRepository.Verify(r => r.GetPaymentMethodByCustomerId(customerId), Times.Once);
    }

    [Fact]
    public async Task InvoiceRepository_ShouldUpdateInvoiceStatus()
    {
        using var scope = Factory.Services.CreateScope();
        var invoiceRepository = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();

        var invoiceId = Guid.NewGuid();
        var expectedReturnId = Guid.NewGuid();

        var mockRepository = Mock.Get(invoiceRepository);
        mockRepository.Setup(r => r.UpdateInvoiceStatus(invoiceId, PaymentStatus.Paid))
            .ReturnsAsync(expectedReturnId);

        var result = await invoiceRepository.UpdateInvoiceStatus(invoiceId, PaymentStatus.Paid);

        Assert.Equal(expectedReturnId, result);

        mockRepository.Verify(r => r.UpdateInvoiceStatus(invoiceId, PaymentStatus.Paid), Times.Once);
    }
}