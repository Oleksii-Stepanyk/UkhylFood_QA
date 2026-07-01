using AutoMapper;
using FluentAssertions;
using KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.PaymentService.BusinessLogic.MappingProfiles;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Services;
using KSE.DistributedSystems.PaymentService.DataAccess;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;
using KSE.DistributedSystems.PaymentService.DataAccess.Interfaces;
using KSE.DistributedSystems.PaymentService.DataAccess.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace KSE.DistributedSystems.PaymentService.Tests;

public class IntegrationTests
{
    private PaymentDbContext _dbContext;
    private IMemoryCache _memoryCache;
    private IServiceProvider _serviceProvider;
    private IPaymentService _paymentService;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddMemoryCache();

        services.AddDbContext<PaymentDbContext>(options =>
            options.UseInMemoryDatabase($"PaymentTestDb_{Guid.NewGuid()}"));

        var mockSendEndpointProvider = new Mock<ISendEndpointProvider>();
        var mockSendEndpoint = new Mock<ISendEndpoint>();
        mockSendEndpointProvider.Setup(x => x.GetSendEndpoint(It.IsAny<Uri>()))
            .ReturnsAsync(mockSendEndpoint.Object);
        services.AddSingleton(mockSendEndpointProvider.Object);

        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<PaymentProfile>());
        var mapper = configuration.CreateMapper();
        services.AddSingleton(mapper);

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentProcessor, PaymentProcessor>();
        services.AddScoped<IPaymentService, PaymentService.BusinessLogic.Services.PaymentService>();
        services.AddScoped<PaymentMonitoringService>();

        var mockRedis = new Mock<StackExchange.Redis.IConnectionMultiplexer>();
        var mockDatabase = new Mock<StackExchange.Redis.IDatabase>();
        mockRedis.Setup(x => x.IsConnected).Returns(true);
        mockRedis.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDatabase.Object);
        services.AddSingleton(mockRedis.Object);

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<PaymentDbContext>();
        _memoryCache = _serviceProvider.GetRequiredService<IMemoryCache>();
        _paymentService = _serviceProvider.GetRequiredService<IPaymentService>();

        await _dbContext.Database.EnsureCreatedAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _dbContext.DisposeAsync();
        _memoryCache?.Dispose();

        if (_serviceProvider is IDisposable disposable)
            disposable.Dispose();
    }

    [Fact]
    public async Task PaymentProcessingFlow_EndToEnd_ShouldWorkCorrectly()
    {
        var paymentRequest = new PaymentRequestDto
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 100.50m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard,
            CardDetails = new PaymentCardDto
            {
                CardNumber = "4111111111111111",
                ExpiryMonth = "12",
                ExpiryYear = "2025",
                Cvv = "123",
                CardholderName = "John Doe"
            }
        };

        var result = await _paymentService.ProcessPaymentAsync(paymentRequest);

        result.Should().NotBeNull();
        result.OrderId.Should().Be(paymentRequest.OrderId);
        result.CustomerId.Should().Be(paymentRequest.CustomerId);
        result.Amount.Should().Be(paymentRequest.Amount);
        result.Currency.Should().Be(paymentRequest.Currency);
        result.Status.Should().BeOneOf(PaymentStatus.Succeeded, PaymentStatus.Failed);

        var retrievedPayment = await _paymentService.GetPaymentAsync(result.Id);
        retrievedPayment.Should().NotBeNull();
        retrievedPayment!.Id.Should().Be(result.Id);
        retrievedPayment!.Status.Should().Be(result.Status);

        var paymentByOrderId = await _paymentService.GetPaymentByOrderIdAsync(paymentRequest.OrderId);
        paymentByOrderId.Should().NotBeNull();
        paymentByOrderId!.Id.Should().Be(result.Id);

        var paymentEntity = await _dbContext.Payments
            .Include(p => p.Events)
            .FirstOrDefaultAsync(p => p.Id == result.Id);

        paymentEntity.Should().NotBeNull();
        paymentEntity!.Events.Should().NotBeEmpty();
        paymentEntity!.Events.Should().Contain(e => e.EventType == PaymentEventType.Created);
        paymentEntity!.Events.Should().Contain(e => e.EventType == PaymentEventType.Processing);

        if (result.Status == PaymentStatus.Succeeded)
        {
            paymentEntity!.Events.Should().Contain(e => e.EventType == PaymentEventType.Succeeded);
        }
        else
        {
            paymentEntity!.Events.Should().Contain(e => e.EventType == PaymentEventType.Failed);
        }
    }

    [Fact]
    public async Task PaymentCaching_ShouldWorkCorrectly()
    {
        var paymentRequest = new PaymentRequestDto
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 75.25m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard,
            CardDetails = new PaymentCardDto
            {
                CardNumber = "4111111111111111",
                ExpiryMonth = "12",
                ExpiryYear = "2025",
                Cvv = "123",
                CardholderName = "Jane Doe"
            }
        };

        var result = await _paymentService.ProcessPaymentAsync(paymentRequest);
        result.Should().NotBeNull();

        var firstRetrieval = await _paymentService.GetPaymentAsync(result.Id);
        firstRetrieval.Should().NotBeNull();

        var secondRetrieval = await _paymentService.GetPaymentAsync(result.Id);
        secondRetrieval.Should().NotBeNull();
        secondRetrieval?.Id.Should().Be(firstRetrieval!.Id);
        secondRetrieval?.Status.Should().Be(firstRetrieval?.Status);

        var cacheKey = $"payment:{result.Id}";
        _memoryCache.TryGetValue(cacheKey, out var cachedValue).Should().BeTrue();
        cachedValue.Should().NotBeNull();
    }

    [Fact]
    public async Task RefundProcessing_ShouldWorkCorrectly()
    {
        var paymentRequest = new PaymentRequestDto
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 200.00m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard,
            CardDetails = new PaymentCardDto
            {
                CardNumber = "4111111111111111",
                ExpiryMonth = "12",
                ExpiryYear = "2025",
                Cvv = "123",
                CardholderName = "Test User"
            }
        };

        var paymentResult = await _paymentService.ProcessPaymentAsync(paymentRequest);
        paymentResult.Should().NotBeNull();

        if (paymentResult.Status == PaymentStatus.Succeeded)
        {
            var refundRequest = new RefundRequestDto
            {
                PaymentId = paymentResult.Id,
                Amount = 50.00m,
                Reason = "Customer requested partial refund"
            };

            var refundResult = await _paymentService.RefundPaymentAsync(refundRequest);
            refundResult.Should().NotBeNull();
            refundResult!.Status.Should().Be(PaymentStatus.PartiallyRefunded);

            var updatedPayment = await _dbContext.Payments
                .Include(p => p.Events)
                .FirstOrDefaultAsync(p => p.Id == paymentResult.Id);

            updatedPayment.Should().NotBeNull();
            updatedPayment!.Status.Should().Be(PaymentStatus.PartiallyRefunded);
            updatedPayment!.RefundedAt.Should().NotBeNull();
            updatedPayment!.Events.Should().Contain(e => e.EventType == PaymentEventType.PartiallyRefunded);
        }
    }

    [Fact]
    public async Task ConcurrentPaymentProcessing_ShouldHandleDuplicates()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var paymentRequest1 = new PaymentRequestDto
        {
            OrderId = orderId,
            CustomerId = customerId,
            Amount = 100.00m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard,
            CardDetails = new PaymentCardDto
            {
                CardNumber = "4111111111111111",
                ExpiryMonth = "12",
                ExpiryYear = "2025",
                Cvv = "123",
                CardholderName = "Test User"
            }
        };

        var paymentRequest2 = new PaymentRequestDto
        {
            OrderId = orderId,
            CustomerId = customerId,
            Amount = 100.00m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard,
            CardDetails = new PaymentCardDto
            {
                CardNumber = "4111111111111111",
                ExpiryMonth = "12",
                ExpiryYear = "2025",
                Cvv = "123",
                CardholderName = "Test User"
            }
        };

        var firstPayment = await _paymentService.ProcessPaymentAsync(paymentRequest1);
        firstPayment.Should().NotBeNull();

        var action = async () => await _paymentService.ProcessPaymentAsync(paymentRequest2);
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Payment already exists for order {orderId}");
    }

    [Fact]
    public async Task PaymentStatusUpdate_ShouldWorkCorrectly()
    {
        var paymentRequest = new PaymentRequestDto
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 150.00m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard,
            CardDetails = new PaymentCardDto
            {
                CardNumber = "4111111111111111",
                ExpiryMonth = "12",
                ExpiryYear = "2025",
                Cvv = "123",
                CardholderName = "Test User"
            }
        };

        var payment = await _paymentService.ProcessPaymentAsync(paymentRequest);
        payment.Should().NotBeNull();

        var updateResult = await _paymentService.UpdatePaymentStatusAsync(
            payment.Id,
            PaymentStatus.Failed,
            "Test failure reason");

        updateResult.Should().BeTrue();

        var updatedPayment = await _paymentService.GetPaymentAsync(payment.Id);
        updatedPayment.Should().NotBeNull();
        updatedPayment?.Status.Should().Be(PaymentStatus.Failed);
        updatedPayment?.FailureReason.Should().Be("Test failure reason");
    }

    [Fact]
    public async Task CustomerPaymentHistory_ShouldWorkCorrectly()
    {
        var customerId = Guid.NewGuid();

        var payment1 = new PaymentRequestDto
        {
            OrderId = Guid.NewGuid(),
            CustomerId = customerId,
            Amount = 50.00m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard,
            CardDetails = new PaymentCardDto
            {
                CardNumber = "4111111111111111",
                ExpiryMonth = "12",
                ExpiryYear = "2025",
                Cvv = "123",
                CardholderName = "Test User"
            }
        };

        var payment2 = new PaymentRequestDto
        {
            OrderId = Guid.NewGuid(),
            CustomerId = customerId,
            Amount = 75.00m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard,
            CardDetails = new PaymentCardDto
            {
                CardNumber = "4111111111111111",
                ExpiryMonth = "12",
                ExpiryYear = "2025",
                Cvv = "123",
                CardholderName = "Test User"
            }
        };

        await _paymentService.ProcessPaymentAsync(payment1);
        await _paymentService.ProcessPaymentAsync(payment2);

        var paymentHistory = await _paymentService.GetCustomerPaymentsAsync(customerId);
        paymentHistory.Should().NotBeEmpty();
        paymentHistory.Should().HaveCount(2);
        paymentHistory.Should().Contain(p => p.Amount == 50.00m);
        paymentHistory.Should().Contain(p => p.Amount == 75.00m);

        var allPaymentHistory = await _paymentService.GetPaymentHistoryAsync(customerId);
        allPaymentHistory.Should().NotBeEmpty();
        allPaymentHistory.Should().HaveCount(2);
    }
}