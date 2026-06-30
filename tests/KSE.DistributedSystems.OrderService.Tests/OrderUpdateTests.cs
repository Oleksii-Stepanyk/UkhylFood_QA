using KSE.DistributedSystems.OrderService.Models;
using Moq;
using Xunit;

namespace KSE.DistributedSystems.OrderService.Tests;

[Trait("Category", "Unit")]
public class OrderUpdateTests : OrderServiceTestBase
{
    [Fact]
    public async Task OnOrderUpdate_ShouldCallUpdateOrderAsync()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending
        };

        MockOrderRepository.Setup(repository =>
            repository.UpdateOrderAsync(order)).ReturnsAsync(order);

        await OrderService.OnOrderUpdate(order, MockPublishEndpoint.Object);

        MockOrderRepository.Verify(repository =>
            repository.UpdateOrderAsync(order), Times.Once);
    }

    [Fact]
    public async Task OnOrderUpdate_ShouldNotPublishEvent()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending
        };

        MockOrderRepository.Setup(repository =>
            repository.UpdateOrderAsync(order)).ReturnsAsync(order);

        await OrderService.OnOrderUpdate(order, MockPublishEndpoint.Object);

        MockPublishEndpoint.Verify(publisher =>
            publisher.Publish(It.IsAny<Order>(), default), Times.Never);
    }
}
