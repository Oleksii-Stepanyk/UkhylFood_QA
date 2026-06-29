using KSE.DistributedSystems.OrderService.DataAccess;
using KSE.DistributedSystems.OrderService.DataAccess.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace KSE.DistributedSystems.OrderService.Tests;

public class BaseApiTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    protected readonly HttpClient Client = factory.CreateClient();
    protected readonly CustomWebApplicationFactory Factory = factory;
}

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RabbitMQ"] = "amqp://localhost:5672",
                ["ConnectionStrings:OrderServiceDb"] = "Host=localhost;Database=test;Username=test;Password=test;"
            });
        });

        builder.ConfigureServices(services =>
        {
            var consulDescriptors = services.Where(d =>
                d.ServiceType.FullName?.Contains("Consul") == true ||
                d.ServiceType.FullName?.Contains("Discovery") == true).ToList();

            foreach (var descriptor in consulDescriptors)
            {
                services.Remove(descriptor);
            }

            var dbContextOptionsDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<OrderDbContext>));
            if (dbContextOptionsDescriptor != null)
                services.Remove(dbContextOptionsDescriptor);

            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(OrderDbContext));
            if (dbContextDescriptor != null)
                services.Remove(dbContextDescriptor);

            var dbContextOptionsBuilderDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptionsBuilder<OrderDbContext>));
            if (dbContextOptionsBuilderDescriptor != null)
                services.Remove(dbContextOptionsBuilderDescriptor);

            var orderRepositoryDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IOrderRepository));
            if (orderRepositoryDescriptor != null)
                services.Remove(orderRepositoryDescriptor);

            var invoiceRepositoryDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IInvoiceRepository));
            if (invoiceRepositoryDescriptor != null)
                services.Remove(invoiceRepositoryDescriptor);

            var paymentRepositoryDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IPaymentRepository));
            if (paymentRepositoryDescriptor != null)
                services.Remove(paymentRepositoryDescriptor);

            var mockOrderRepository = new Mock<IOrderRepository>();
            var mockInvoiceRepository = new Mock<IInvoiceRepository>();
            var mockPaymentRepository = new Mock<IPaymentRepository>();

            services.AddSingleton(mockOrderRepository.Object);
            services.AddSingleton(mockInvoiceRepository.Object);
            services.AddSingleton(mockPaymentRepository.Object);

            var redisRegistration = services.SingleOrDefault(
                d => d.ServiceType == typeof(IConnectionMultiplexer));

            if (redisRegistration != null)
                services.Remove(redisRegistration);

            var mockConn = new Mock<IConnectionMultiplexer>();
            mockConn.Setup(m => m.IsConnected).Returns(true);

            var mockRedisDb = new Mock<IDatabase>();
            mockConn.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockRedisDb.Object);

            services.AddSingleton(mockConn.Object);

            mockRedisDb.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)"0");

            mockRedisDb.Setup(db => db.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(1);

            mockRedisDb.Setup(db => db.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
        });

        builder.UseEnvironment("Testing");
    }
}