using System;
using System.Linq;
using System.Threading.Tasks;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using KSE.DistributedSystems.OrderService.Services;
using KSE.DistributedSystems.OrderService.Services.Interfaces;
using KSE.DistributedSystems.OrderService.Models;
using KSE.DistributedSystems.OrderService.DataAccess.Repositories;

namespace KSE.DistributedSystems.OrderService.Tests.ContractTests;

[Trait("Category", "Contract")]
public class OrderCreatedProducerContractTests : IAsyncLifetime
{
    private readonly InMemoryTestHarness _harness;
    private readonly Mock<IOrderRepository> _mockOrderRepo;
    private readonly Mock<IPaymentRepository> _mockPaymentRepo;
    private readonly Mock<IInvoiceRepository> _mockInvoiceRepo;
    private readonly Mock<ILogger<OrderMonitoringService>> _mockMonitoringLogger;
    private readonly OrderMonitoringService _metricsService;
    private readonly Services.OrderService _orderService;

    public OrderCreatedProducerContractTests()
    {
        _harness = new InMemoryTestHarness();

        _mockOrderRepo = new Mock<IOrderRepository>();
        _mockPaymentRepo = new Mock<IPaymentRepository>();
        _mockInvoiceRepo = new Mock<IInvoiceRepository>();
        _mockMonitoringLogger = new Mock<ILogger<OrderMonitoringService>>();
        _metricsService = new OrderMonitoringService(_mockMonitoringLogger.Object);

        _orderService = new Services.OrderService(
            _mockOrderRepo.Object,
            _mockPaymentRepo.Object,
            _mockInvoiceRepo.Object,
            _metricsService
        );
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
    public async Task OnOrderPlaced_ShouldPublishOrderCreatedContract()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var order = new Order { Id = orderId, CustomerId = customerId, TotalPrice = 50.0f };
        var paymentMethod = new PaymentMethod { CustomerId = customerId };

        _mockOrderRepo.Setup(repository =>
            repository.CreateOrderAsync(It.IsAny<Order>())).ReturnsAsync((Order order) => order);
        _mockPaymentRepo.Setup(repository =>
            repository.GetPaymentMethodByCustomerId(customerId)).ReturnsAsync(paymentMethod);

        // Act
        await _orderService.OnOrderPlaced(order, _harness.Bus);

        // Assert
        bool isPublished = await _harness.Published.Any<KSE.DistributedSystems.NotificationService.Models.OrderCreated>();

        Assert.True(isPublished, "OrderService did not publish the expected OrderCreated contract message.");
    }
}
