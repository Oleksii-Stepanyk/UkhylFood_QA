using System;
using System.Linq;
using System.Threading.Tasks;
using MassTransit.Testing;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using KSE.DistributedSystems.OrderService.Consumers;
using KSE.DistributedSystems.OrderService.Models;
using KSE.DistributedSystems.OrderService.Services;
using KSE.DistributedSystems.OrderService.Services.Interfaces;

namespace KSE.DistributedSystems.OrderService.Tests.ContractTests;

[Trait("Category", "Contract")]
public class PaymentResultConsumerContractTests : IAsyncLifetime
{
    private readonly InMemoryTestHarness _harness;
    private readonly Mock<IOrderService> _mockOrderService;
    private readonly Mock<ILogger<PaymentResultConsumer>> _mockLogger;
    private readonly Mock<ILogger<OrderMonitoringService>> _mockMonitoringLogger;
    private readonly OrderMonitoringService _metricsService;
    private readonly ConsumerTestHarness<PaymentResultConsumer> _consumerHarness;

    public PaymentResultConsumerContractTests()
    {
        _harness = new InMemoryTestHarness();

        _mockOrderService = new Mock<IOrderService>();
        _mockLogger = new Mock<ILogger<PaymentResultConsumer>>();
        _mockMonitoringLogger = new Mock<ILogger<OrderMonitoringService>>();
        _metricsService = new OrderMonitoringService(_mockMonitoringLogger.Object);

        _consumerHarness = _harness.Consumer(() => new PaymentResultConsumer(
            _mockLogger.Object,
            _mockOrderService.Object,
            _metricsService
        ));
    }

    public async Task InitializeAsync()
    {
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
    }

    [Fact]
    public async Task Consume_ShouldSuccessfullyDeserializeAndProcess_PaymentResultContract()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var contractMessage = new PaymentResult { OrderId = orderId, Status = (int)PaymentStatus.Paid };

        // Act
        await _harness.InputQueueSendEndpoint.Send(contractMessage);

        // Assert contract
        Assert.True(await _consumerHarness.Consumed.Any<PaymentResult>());

        _mockOrderService.Verify(service => service.OnPaymentSuccess(
            It.Is<PaymentResult>(result => result.OrderId == orderId && result.Status == (int)PaymentStatus.Paid),
            It.IsAny<MassTransit.ConsumeContext<PaymentResult>>()),
            Times.Once);
    }
}
