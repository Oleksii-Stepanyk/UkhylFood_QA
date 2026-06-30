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
            var consulDescriptors = services.Where(descriptor =>
                descriptor.ServiceType.FullName?.Contains("Consul") == true ||
                descriptor.ServiceType.FullName?.Contains("Discovery") == true).ToList();

            foreach (var descriptor in consulDescriptors)
            {
                services.Remove(descriptor);
            }

            var dbContextOptionsDescriptor = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(DbContextOptions<OrderDbContext>));
            if (dbContextOptionsDescriptor != null)
                services.Remove(dbContextOptionsDescriptor);

            var dbContextDescriptor = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(OrderDbContext));
            if (dbContextDescriptor != null)
                services.Remove(dbContextDescriptor);

            var dbContextOptionsBuilderDescriptor = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(DbContextOptionsBuilder<OrderDbContext>));
            if (dbContextOptionsBuilderDescriptor != null)
                services.Remove(dbContextOptionsBuilderDescriptor);

            var orderRepositoryDescriptor = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(IOrderRepository));
            if (orderRepositoryDescriptor != null)
                services.Remove(orderRepositoryDescriptor);

            var invoiceRepositoryDescriptor = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(IInvoiceRepository));
            if (invoiceRepositoryDescriptor != null)
                services.Remove(invoiceRepositoryDescriptor);

            var paymentRepositoryDescriptor = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(IPaymentRepository));
            if (paymentRepositoryDescriptor != null)
                services.Remove(paymentRepositoryDescriptor);

            var mockOrderRepository = new Mock<IOrderRepository>();
            var mockInvoiceRepository = new Mock<IInvoiceRepository>();
            var mockPaymentRepository = new Mock<IPaymentRepository>();

            services.AddSingleton(mockOrderRepository.Object);
            services.AddSingleton(mockInvoiceRepository.Object);
            services.AddSingleton(mockPaymentRepository.Object);

            var redisRegistration = services.SingleOrDefault(
                descriptor => descriptor.ServiceType == typeof(IConnectionMultiplexer));

            if (redisRegistration != null)
                services.Remove(redisRegistration);

            var mockConn = new Mock<IConnectionMultiplexer>();
            mockConn.Setup(muxer => muxer.IsConnected).Returns(true);

            var mockRedisDb = new Mock<IDatabase>();
            mockConn.Setup(muxer => muxer.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockRedisDb.Object);

            services.AddSingleton(mockConn.Object);

            mockRedisDb.Setup(database => database.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync((RedisValue)"0");

            mockRedisDb.Setup(database => database.StringIncrementAsync(It.IsAny<RedisKey>(), It.IsAny<long>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(1);

            mockRedisDb.Setup(database => database.KeyExpireAsync(It.IsAny<RedisKey>(), It.IsAny<TimeSpan>(), It.IsAny<CommandFlags>()))
                .ReturnsAsync(true);
        });

        builder.UseEnvironment("Testing");
    }
}