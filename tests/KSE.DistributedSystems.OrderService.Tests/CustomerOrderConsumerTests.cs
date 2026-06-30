using KSE.DistributedSystems.OrderService.Consumers;
using KSE.DistributedSystems.OrderService.DataAccess.Repositories;
using KSE.DistributedSystems.OrderService.Models;
using KSE.DistributedSystems.OrderService.Services;
using KSE.DistributedSystems.OrderService.Services.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace KSE.DistributedSystems.OrderService.Tests;

[Trait("Category", "Unit")]
public class CustomerOrderConsumerTests
{
    private readonly Mock<IOrderService> _mockOrderService;
    private readonly Mock<IOrderRepository> _mockRepository;
    private readonly Mock<ILogger<CustomerOrderConsumer>> _mockLogger;
    private readonly Mock<ILogger<OrderMonitoringService>> _mockMonitoringLogger;
    private readonly OrderMonitoringService _metricsService;
    private readonly CustomerOrderConsumer _consumer;
    private readonly Mock<ConsumeContext<Order>> _mockContext;

    public CustomerOrderConsumerTests()
    {
        _mockOrderService = new Mock<IOrderService>();
        _mockRepository = new Mock<IOrderRepository>();
        _mockLogger = new Mock<ILogger<CustomerOrderConsumer>>();
        _mockMonitoringLogger = new Mock<ILogger<OrderMonitoringService>>();
        _metricsService = new OrderMonitoringService(_mockMonitoringLogger.Object);
        _consumer = new CustomerOrderConsumer(_mockOrderService.Object, _mockRepository.Object, _mockLogger.Object, _metricsService);
        _mockContext = new Mock<ConsumeContext<Order>>();
    }

    [Fact]
    public async Task Consume_ShouldCallOnOrderPlaced_WhenOrderDoesNotExistAndStatusIsPending()
    {
        var order = new Order { Id = Guid.NewGuid(), Status = OrderStatus.Pending };
        _mockContext.Setup(context => context.Message).Returns(order);
        _mockRepository.Setup(repo => repo.ExistsAsync(order.Id)).ReturnsAsync(false);

        await _consumer.Consume(_mockContext.Object);

        _mockOrderService.Verify(service => service.OnOrderPlaced(order, _mockContext.Object), Times.Once);
        _mockOrderService.Verify(service => service.OnOrderUpdate(It.IsAny<Order>(), It.IsAny<IPublishEndpoint>()), Times.Never);
    }

    [Fact]
    public async Task Consume_ShouldCallOnOrderUpdate_WhenOrderExists()
    {
        var order = new Order { Id = Guid.NewGuid(), Status = OrderStatus.Pending };
        _mockContext.Setup(context => context.Message).Returns(order);
        _mockRepository.Setup(repo => repo.ExistsAsync(order.Id)).ReturnsAsync(true);

        await _consumer.Consume(_mockContext.Object);

        _mockOrderService.Verify(service => service.OnOrderUpdate(order, _mockContext.Object), Times.Once);
        _mockOrderService.Verify(service => service.OnOrderPlaced(It.IsAny<Order>(), It.IsAny<IPublishEndpoint>()), Times.Never);
    }

    [Fact]
    public async Task Consume_ShouldIgnore_WhenOrderDoesNotExistAndStatusIsNotPending()
    {
        var order = new Order { Id = Guid.NewGuid(), Status = OrderStatus.Confirmed };
        _mockContext.Setup(context => context.Message).Returns(order);
        _mockRepository.Setup(repo => repo.ExistsAsync(order.Id)).ReturnsAsync(false);

        await _consumer.Consume(_mockContext.Object);

        _mockOrderService.Verify(service => service.OnOrderPlaced(It.IsAny<Order>(), It.IsAny<IPublishEndpoint>()), Times.Never);
        _mockOrderService.Verify(service => service.OnOrderUpdate(It.IsAny<Order>(), It.IsAny<IPublishEndpoint>()), Times.Never);
    }

    [Fact]
    public async Task Consume_ShouldRethrowNullReferenceException_WhenMessageIsNull()
    {
        _mockContext.Setup(context => context.Message).Returns((Order)null!);

        await Assert.ThrowsAsync<NullReferenceException>(() => _consumer.Consume(_mockContext.Object));
        
        _mockOrderService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Consume_ShouldRethrowException_WhenServiceThrows()
    {
        var order = new Order { Id = Guid.NewGuid(), Status = OrderStatus.Pending };
        _mockContext.Setup(context => context.Message).Returns(order);
        _mockRepository.Setup(repo => repo.ExistsAsync(order.Id)).ReturnsAsync(false);
        _mockOrderService.Setup(service => service.OnOrderPlaced(order, _mockContext.Object)).ThrowsAsync(new Exception("Test exception"));

        await Assert.ThrowsAsync<Exception>(() => _consumer.Consume(_mockContext.Object));
    }
}
