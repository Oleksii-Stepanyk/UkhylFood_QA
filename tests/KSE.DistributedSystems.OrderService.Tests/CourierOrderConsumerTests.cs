using KSE.DistributedSystems.OrderService.Consumers;
using KSE.DistributedSystems.OrderService.Models;
using KSE.DistributedSystems.OrderService.Services.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace KSE.DistributedSystems.OrderService.Tests;

[Trait("Category", "Unit")]
public class CourierOrderConsumerTests
{
    private readonly Mock<IOrderService> _mockOrderService;
    private readonly Mock<ILogger<CourierOrderConsumer>> _mockLogger;
    private readonly CourierOrderConsumer _consumer;
    private readonly Mock<ConsumeContext<Order>> _mockContext;

    public CourierOrderConsumerTests()
    {
        _mockOrderService = new Mock<IOrderService>();
        _mockLogger = new Mock<ILogger<CourierOrderConsumer>>();
        _consumer = new CourierOrderConsumer(_mockLogger.Object, _mockOrderService.Object);
        _mockContext = new Mock<ConsumeContext<Order>>();
    }

    [Fact]
    public async Task Consume_ShouldCallOnOrderUpdate()
    {
        var order = new Order { Id = Guid.NewGuid(), Status = OrderStatus.Pending };
        _mockContext.Setup(context => context.Message).Returns(order);

        await _consumer.Consume(_mockContext.Object);

        _mockOrderService.Verify(service => service.OnOrderUpdate(order, _mockContext.Object), Times.Once);
    }

    [Fact]
    public async Task Consume_ShouldNotRethrowException_WhenServiceThrows()
    {
        var order = new Order { Id = Guid.NewGuid(), Status = OrderStatus.Pending };
        _mockContext.Setup(context => context.Message).Returns(order);
        _mockOrderService.Setup(service => service.OnOrderUpdate(order, _mockContext.Object)).ThrowsAsync(new Exception("Test exception"));

        // Should not throw since catch block does not rethrow
        await _consumer.Consume(_mockContext.Object);
        
        // Verify logger was called
        _mockLogger.Verify(
            logger => logger.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, type) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((value, type) => true)),
            Times.Once);
    }
}
