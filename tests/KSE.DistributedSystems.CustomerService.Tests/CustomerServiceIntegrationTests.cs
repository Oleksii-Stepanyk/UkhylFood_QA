using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using KSE.DistributedSystems.CustomerService.DataAccess;
using KSE.DistributedSystems.CustomerService.DataAccess.Entities;
using KSE.DistributedSystems.CustomerService.DataAccess.Models;
using KSE.DistributedSystems.CustomerService.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace KSE.DistributedSystems.CustomerService.Tests;

[TestFixture]
public class CustomerServiceIntegrationTests
{
    private CustomCustomerWebApplicationFactory _factory;
    private HttpClient _client;
    private Mock<ICacheService> _mockCache;

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        _mockCache = new Mock<ICacheService>();
        _factory = new CustomCustomerWebApplicationFactory(_mockCache);
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [SetUp]
    public void Setup()
    {
        _mockCache.Reset();
    }

    [Test]
    public async Task GetCustomer_WhenCustomerExists_ShouldReturnOkWithCustomer()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new CustomerDTO(customerId, "Alice Jones", "alice@example.com", "1234567890", "123 Cherry Lane", 500, null);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            var dbEntity = new Customer
            {
                Id = customer.Id,
                Name = customer.Name,
                Email = customer.Email,
                PhoneNumber = customer.PhoneNumber,
                Address = customer.Address,
                LoyaltyPoints = customer.LoyaltyPoints
            };
            dbContext.Customers.Add(dbEntity);
            await dbContext.SaveChangesAsync();
        }

        // Mock cache to return null so it goes to DB
        _mockCache.Setup(c => c.GetAsync<CustomerDTO>($"customer:{customerId}"))
            .ReturnsAsync((CustomerDTO?)null);

        // Act
        var response = await _client.GetAsync($"/customers/{customerId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var returnedCustomer = await response.Content.ReadFromJsonAsync<CustomerDTO>();
        Assert.That(returnedCustomer, Is.Not.Null);
        Assert.That(returnedCustomer!.Id, Is.EqualTo(customerId));
        Assert.That(returnedCustomer.Name, Is.EqualTo("Alice Jones"));
    }

    [Test]
    public async Task GetCustomer_WhenCustomerDoesNotExist_ShouldReturnNotFound()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        _mockCache.Setup(c => c.GetAsync<CustomerDTO>($"customer:{customerId}"))
            .ReturnsAsync((CustomerDTO?)null);

        // Act
        var response = await _client.GetAsync($"/customers/{customerId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task UpdateCustomer_WithValidData_ShouldReturnNoContent()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new CustomerDTO(customerId, "Bob Logan", "bob@example.com", "0987654321", "789 Elm St", 300, null);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            var dbEntity = new Customer
            {
                Id = customer.Id,
                Name = "Old Name",
                Email = customer.Email,
                PhoneNumber = customer.PhoneNumber,
                Address = customer.Address,
                LoyaltyPoints = customer.LoyaltyPoints
            };
            dbContext.Customers.Add(dbEntity);
            await dbContext.SaveChangesAsync();
        }

        // Act
        var response = await _client.PutAsJsonAsync($"/customers/{customerId}", customer);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            var updated = await dbContext.Customers.FindAsync(customerId);
            Assert.That(updated, Is.Not.Null);
            Assert.That(updated!.Name, Is.EqualTo("Bob Logan"));
        }
    }

    [Test]
    public async Task UpdateCustomer_WithMismatchedId_ShouldReturnBadRequest()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new CustomerDTO(Guid.NewGuid(), "Bob Logan", "bob@example.com", "0987654321", "789 Elm St", 300, null);

        // Act
        var response = await _client.PutAsJsonAsync($"/customers/{customerId}", customer);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}

public class CustomCustomerWebApplicationFactory : WebApplicationFactory<KSE.DistributedSystems.CustomerService.Program>
{
    private readonly Mock<ICacheService> _mockCache;
    private readonly string _dbName = $"CustomerTestDb_{Guid.NewGuid()}";

    public CustomCustomerWebApplicationFactory(Mock<ICacheService> mockCache)
    {
        _mockCache = mockCache;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["ConnectionStrings:DbContext"] = "Host=localhost;Database=test;Username=test;Password=test;",
                ["RabbitMQ:Host"] = "rabbitmq://localhost"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove real DbContext
            var toRemove = services.Where(d => 
                d.ServiceType.FullName?.Contains("CustomerDbContext") == true ||
                d.ServiceType.FullName?.Contains("DbContextOptions") == true).ToList();
            foreach (var descriptor in toRemove)
            {
                services.Remove(descriptor);
            }

            // Add InMemory
            services.AddDbContext<CustomerDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
            });

            // Remove real CacheService
            var cacheServiceDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(ICacheService));
            if (cacheServiceDescriptor != null)
                services.Remove(cacheServiceDescriptor);

            // Add mocked CacheService
            services.AddSingleton(_mockCache.Object);
        });

        builder.UseEnvironment("Testing");
    }
}
