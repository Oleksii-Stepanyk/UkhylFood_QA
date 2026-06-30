using KSE.DistributedSystems.OrderService.DataAccess.Repositories;
using KSE.DistributedSystems.OrderService.Services;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;

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
