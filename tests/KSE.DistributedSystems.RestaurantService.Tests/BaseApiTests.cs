using KSE.DistributedSystems.RestaurantService.DataAccess;
using KSE.DistributedSystems.RestaurantService.DataAccess.Models;
using KSE.DistributedSystems.RestaurantService.DataAccess.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using Microsoft.EntityFrameworkCore.InMemory;

namespace KSE.DistributedSystems.RestaurantService.Tests;

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
                ["ConnectionStrings:RestaurantServiceDb"] = "Host=localhost;Database=test;Username=test;Password=test;"
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
                d => d.ServiceType == typeof(DbContextOptions<RestaurantDbContext>));
            if (dbContextOptionsDescriptor != null)
                services.Remove(dbContextOptionsDescriptor);

            var dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(RestaurantDbContext));
            if (dbContextDescriptor != null)
                services.Remove(dbContextDescriptor);

            var dbContextOptionsBuilderDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptionsBuilder<RestaurantDbContext>));
            if (dbContextOptionsBuilderDescriptor != null)
                services.Remove(dbContextOptionsBuilderDescriptor);

            var orderRepositoryDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IOrderRepository));
            if (orderRepositoryDescriptor != null)
                services.Remove(orderRepositoryDescriptor);

            var mockOrderRepository = new Mock<IOrderRepository>();

            services.AddSingleton(mockOrderRepository.Object);
            
            services.AddDbContext<RestaurantDbContext>(options =>
            {
                options.UseInMemoryDatabase("TestDb");
            });
        });

        builder.UseEnvironment("Testing");
    }
}