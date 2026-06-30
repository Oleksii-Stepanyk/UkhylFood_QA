using System;
using System.Threading.Tasks;
using KSE.DistributedSystems.CustomerService.DataAccess.Models;
using KSE.DistributedSystems.CustomerService.DataAccess.Repositories;
using KSE.DistributedSystems.CustomerService.Services;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace KSE.DistributedSystems.CustomerService.Tests;

[TestFixture]
public class CustomerServiceUnitTests
{
    private Mock<ICustomerRepository> _mockRepository;
    private Mock<ICacheService> _mockCacheService;
    private Mock<ILogger<Services.CustomerService>> _mockLogger;
    private Mock<ILogger<CustomerMonitoringService>> _mockMonitoringLogger;
    private CustomerMonitoringService _monitoringService;
    private Services.CustomerService _customerService;

    [SetUp]
    public void Setup()
    {
        _mockRepository = new Mock<ICustomerRepository>();
        _mockCacheService = new Mock<ICacheService>();
        _mockLogger = new Mock<ILogger<Services.CustomerService>>();
        _mockMonitoringLogger = new Mock<ILogger<CustomerMonitoringService>>();
        _monitoringService = new CustomerMonitoringService(_mockMonitoringLogger.Object);
        _customerService = new Services.CustomerService(
            _mockRepository.Object,
            _mockCacheService.Object,
            _mockLogger.Object,
            _monitoringService
        );
    }

    [TearDown]
    public void TearDown()
    {
        _monitoringService.Dispose();
    }

    [Test]
    public async Task GetCustomerAsync_WhenCached_ShouldReturnCachedCustomerAndNotCallRepository()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var cachedCustomer = new CustomerDTO(customerId, "John Doe", "john@example.com", "12345", "123 Main St", 100, null);
        var cacheKey = $"customer:{customerId}";

        _mockCacheService.Setup(c => c.GetAsync<CustomerDTO>(cacheKey))
            .ReturnsAsync(cachedCustomer);

        // Act
        var result = await _customerService.GetCustomerAsync(customerId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(customerId));
        Assert.That(result.Name, Is.EqualTo("John Doe"));
        
        _mockRepository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
        _mockCacheService.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<CustomerDTO>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Test]
    public async Task GetCustomerAsync_WhenNotCachedButExistsInDb_ShouldReturnFromDbAndSaveToCache()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var dbCustomer = new CustomerDTO(customerId, "Jane Doe", "jane@example.com", "54321", "456 Oak Ave", 250, null);
        var cacheKey = $"customer:{customerId}";

        _mockCacheService.Setup(c => c.GetAsync<CustomerDTO>(cacheKey))
            .ReturnsAsync((CustomerDTO?)null);
        _mockRepository.Setup(r => r.GetByIdAsync(customerId))
            .ReturnsAsync(dbCustomer);
        _mockCacheService.Setup(c => c.SetAsync(cacheKey, dbCustomer, It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _customerService.GetCustomerAsync(customerId);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(customerId));
        Assert.That(result.Name, Is.EqualTo("Jane Doe"));

        _mockRepository.Verify(r => r.GetByIdAsync(customerId), Times.Once);
        _mockCacheService.Verify(c => c.SetAsync(cacheKey, dbCustomer, It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Test]
    public async Task GetCustomerAsync_WhenNotCachedAndNotExistsInDb_ShouldReturnNullAndNotCache()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var cacheKey = $"customer:{customerId}";

        _mockCacheService.Setup(c => c.GetAsync<CustomerDTO>(cacheKey))
            .ReturnsAsync((CustomerDTO?)null);
        _mockRepository.Setup(r => r.GetByIdAsync(customerId))
            .ReturnsAsync((CustomerDTO?)null);

        // Act
        var result = await _customerService.GetCustomerAsync(customerId);

        // Assert
        Assert.That(result, Is.Null);

        _mockRepository.Verify(r => r.GetByIdAsync(customerId), Times.Once);
        _mockCacheService.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<CustomerDTO>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Test]
    public async Task UpdateCustomerAsync_ShouldUpdateRepositoryAndSaveToCache()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var customer = new CustomerDTO(customerId, "Bob Smith", "bob@example.com", "11111", "789 Pine Rd", 50, null);
        var cacheKey = $"customer:{customerId}";

        _mockRepository.Setup(r => r.UpdateAsync(customer))
            .ReturnsAsync(customer);
        _mockCacheService.Setup(c => c.SetAsync(cacheKey, customer, It.IsAny<TimeSpan?>()))
            .Returns(Task.CompletedTask);

        // Act
        await _customerService.UpdateCustomerAsync(customerId, customer);

        // Assert
        _mockRepository.Verify(r => r.UpdateAsync(customer), Times.Once);
        _mockCacheService.Verify(c => c.SetAsync(cacheKey, customer, It.IsAny<TimeSpan?>()), Times.Once);
    }
}