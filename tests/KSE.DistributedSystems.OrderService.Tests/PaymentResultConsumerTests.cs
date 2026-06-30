using KSE.DistributedSystems.OrderService.Consumers;
using KSE.DistributedSystems.OrderService.Models;
using KSE.DistributedSystems.OrderService.Services;
using KSE.DistributedSystems.OrderService.Services.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace KSE.DistributedSystems.OrderService.Tests;

[Trait("Category", "Unit")]
public class PaymentResultConsumerTests
{
    private readonly Mock<IOrderService> _mockOrderService;
    private readonly Mock<ILogger<PaymentResultConsumer>> _mockLogger;
    private readonly Mock<ILogger<OrderMonitoringService>> _mockMonitoringLogger;
    private readonly OrderMonitoringService _metricsService;
    private readonly PaymentResultConsumer _consumer;
    private readonly Mock<ConsumeContext<PaymentResult>> _mockContext;

    public PaymentResultConsumerTests()
    {
        _mockOrderService = new Mock<IOrderService>();
        _mockLogger = new Mock<ILogger<PaymentResultConsumer>>();
        _mockMonitoringLogger = new Mock<ILogger<OrderMonitoringService>>();
        _metricsService = new OrderMonitoringService(_mockMonitoringLogger.Object);
        _consumer = new PaymentResultConsumer(_mockLogger.Object, _mockOrderService.Object, _metricsService);
        _mockContext = new Mock<ConsumeContext<PaymentResult>>();
    }

    [Fact]
    public async Task Consume_ShouldCallOnPaymentFail_WhenStatusIsFailed()
    {
        var result = new PaymentResult { OrderId = Guid.NewGuid(), Status = (int)PaymentStatus.Failed };
        _mockContext.Setup(context => context.Message).Returns(result);

        await _consumer.Consume(_mockContext.Object);

        _mockOrderService.Verify(service => service.OnPaymentFail(result, _mockContext.Object), Times.Once);
        _mockOrderService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Consume_ShouldCallOnPaymentSuccess_WhenStatusIsPaid()
    {
        var result = new PaymentResult { OrderId = Guid.NewGuid(), Status = (int)PaymentStatus.Paid };
        _mockContext.Setup(context => context.Message).Returns(result);

        await _consumer.Consume(_mockContext.Object);

        _mockOrderService.Verify(service => service.OnPaymentSuccess(result, _mockContext.Object), Times.Once);
        _mockOrderService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Consume_ShouldNotCallService_WhenStatusIsPending()
    {
        var result = new PaymentResult { OrderId = Guid.NewGuid(), Status = (int)PaymentStatus.Pending };
        _mockContext.Setup(context => context.Message).Returns(result);

        await _consumer.Consume(_mockContext.Object);

        _mockOrderService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Consume_ShouldNotCallService_WhenMessageIsNull()
    {
        _mockContext.Setup(context => context.Message).Returns((PaymentResult)null!);

        await _consumer.Consume(_mockContext.Object);

        _mockOrderService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Consume_ShouldRethrowException_WhenServiceThrows()
    {
        var result = new PaymentResult { OrderId = Guid.NewGuid(), Status = (int)PaymentStatus.Paid };
        _mockContext.Setup(context => context.Message).Returns(result);
        _mockOrderService.Setup(service => service.OnPaymentSuccess(result, _mockContext.Object)).ThrowsAsync(new Exception("Test exception"));

        await Assert.ThrowsAsync<Exception>(() => _consumer.Consume(_mockContext.Object));
    }
}
