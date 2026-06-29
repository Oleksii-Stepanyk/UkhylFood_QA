using AutoMapper;
using FluentAssertions;
using KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.PaymentService.BusinessLogic.MappingProfiles;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Services;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;
using KSE.DistributedSystems.PaymentService.DataAccess.Interfaces;
using MassTransit;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace KSE.DistributedSystems.PaymentService.Tests;

public class PaymentServiceUnitTests
{
    private Mock<IPaymentRepository> _repositoryMock;
    private Mock<IPaymentProcessor> _processorMock;
    private Mock<ILogger<PaymentService.BusinessLogic.Services.PaymentService>> _loggerMock;
    private IMemoryCache _memoryCache;
    private Mock<StackExchange.Redis.IConnectionMultiplexer> _redisMock;
    private Mock<StackExchange.Redis.IDatabase> _databaseMock;
    private Mock<ISendEndpointProvider> _sendEndpointProviderMock;
    private IMapper _mapper;
    private PaymentService.BusinessLogic.Services.PaymentService _paymentService;

    [SetUp]
    public void Setup()
    {
        _repositoryMock = new Mock<IPaymentRepository>();
        _processorMock = new Mock<IPaymentProcessor>();
        _loggerMock = new Mock<ILogger<PaymentService.BusinessLogic.Services.PaymentService>>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _redisMock = new Mock<StackExchange.Redis.IConnectionMultiplexer>();
        _databaseMock = new Mock<StackExchange.Redis.IDatabase>();
        _sendEndpointProviderMock = new Mock<ISendEndpointProvider>();

        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<PaymentProfile>());
        _mapper = configuration.CreateMapper();

        _redisMock.Setup(x => x.IsConnected).Returns(true);
        _redisMock.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_databaseMock.Object);

        var mockSendEndpoint = new Mock<ISendEndpoint>();
        _sendEndpointProviderMock.Setup(x => x.GetSendEndpoint(It.IsAny<Uri>()))
            .ReturnsAsync(mockSendEndpoint.Object);

        var monitorService = new PaymentMonitoringService(new Mock<ILogger<PaymentMonitoringService>>().Object);

        _paymentService = new PaymentService.BusinessLogic.Services.PaymentService(
            _repositoryMock.Object,
            _processorMock.Object,
            _mapper,
            _loggerMock.Object,
            _memoryCache,
            _redisMock.Object,
            _sendEndpointProviderMock.Object,
            monitorService);
    }

    [TearDown]
    public void TearDown()
    {
        _memoryCache?.Dispose();
    }

    [Test]
    public async Task ProcessPaymentAsync_ValidRequest_ShouldReturnSuccessResponse()
    {
        var request = new PaymentRequestDto
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 100.50m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard
        };

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = request.OrderId,
            CustomerId = request.CustomerId,
            Amount = request.Amount,
            Currency = request.Currency,
            PaymentMethod = request.PaymentMethod,
            Status = PaymentStatus.Processing
        };

        var processingResult = new PaymentProcessingResult
        {
            IsSuccess = true,
            ExternalPaymentId = "ext_123456",
            ProcessorResponse = new Dictionary<string, string>()
        };

        _repositoryMock.Setup(x => x.GetByOrderIdAsync(request.OrderId))
            .ReturnsAsync((Payment?)null);
        _repositoryMock.Setup(x => x.AddAsync(It.IsAny<Payment>()))
            .ReturnsAsync(payment);
        _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Payment>()))
            .ReturnsAsync(payment);
        _repositoryMock.Setup(x => x.AddEventAsync(It.IsAny<PaymentEvent>()))
            .Returns(Task.CompletedTask);
        _processorMock.Setup(x => x.ProcessAsync(It.IsAny<Payment>()))
            .ReturnsAsync(processingResult);

        var result = await _paymentService.ProcessPaymentAsync(request);

        result.Should().NotBeNull();
        result.OrderId.Should().Be(request.OrderId);
        result.Amount.Should().Be(request.Amount);
        result.Status.Should().Be(PaymentStatus.Succeeded);
        _repositoryMock.Verify(x => x.AddAsync(It.IsAny<Payment>()), Times.Once);
        _processorMock.Verify(x => x.ProcessAsync(It.IsAny<Payment>()), Times.Once);
    }

    [Test]
    public async Task ProcessPaymentAsync_DuplicatePayment_ShouldThrowInvalidOperationException()
    {
        var request = new PaymentRequestDto
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 100.50m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard
        };

        var existingPayment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = request.OrderId,
            Status = PaymentStatus.Succeeded
        };

        _repositoryMock.Setup(x => x.GetByOrderIdAsync(request.OrderId))
            .ReturnsAsync(existingPayment);

        var act = async () => await _paymentService.ProcessPaymentAsync(request);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Payment already exists for order {request.OrderId}");
    }

    [Test]
    public async Task ProcessPaymentAsync_ProcessorFailure_ShouldReturnFailedStatus()
    {
        var request = new PaymentRequestDto
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 100.50m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard
        };

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = request.OrderId,
            CustomerId = request.CustomerId,
            Amount = request.Amount,
            Currency = request.Currency,
            PaymentMethod = request.PaymentMethod,
            Status = PaymentStatus.Processing
        };

        var processingResult = new PaymentProcessingResult
        {
            IsSuccess = false,
            ErrorMessage = "Card declined",
            ProcessorResponse = new Dictionary<string, string>()
        };

        _repositoryMock.Setup(x => x.GetByOrderIdAsync(request.OrderId))
            .ReturnsAsync((Payment?)null);
        _repositoryMock.Setup(x => x.AddAsync(It.IsAny<Payment>()))
            .ReturnsAsync(payment);
        _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Payment>()))
            .ReturnsAsync(payment);
        _repositoryMock.Setup(x => x.AddEventAsync(It.IsAny<PaymentEvent>()))
            .Returns(Task.CompletedTask);
        _processorMock.Setup(x => x.ProcessAsync(It.IsAny<Payment>()))
            .ReturnsAsync(processingResult);

        var result = await _paymentService.ProcessPaymentAsync(request);

        result.Should().NotBeNull();
        result.Status.Should().Be(PaymentStatus.Failed);
        result.FailureReason.Should().Be("Card declined");
    }

    [Test]
    public async Task GetPaymentAsync_ValidId_ShouldReturnPayment()
    {
        var paymentId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = paymentId,
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 100.50m,
            Currency = "USD",
            Status = PaymentStatus.Succeeded
        };

        _repositoryMock.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync(payment);

        var result = await _paymentService.GetPaymentAsync(paymentId);

        result.Should().NotBeNull();
        result?.Id.Should().Be(paymentId);
        result?.Amount.Should().Be(100.50m);
        result?.Status.Should().Be(PaymentStatus.Succeeded);
    }

    [Test]
    public async Task GetPaymentAsync_InvalidId_ShouldReturnNull()
    {
        var paymentId = Guid.NewGuid();

        _repositoryMock.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync((Payment?)null);

        var result = await _paymentService.GetPaymentAsync(paymentId);

        result.Should().BeNull();
    }

    [Test]
    public async Task GetPaymentAsync_EmptyId_ShouldThrowArgumentException()
    {
        var act = async () => await _paymentService.GetPaymentAsync(Guid.Empty);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Payment ID cannot be empty*");
    }

    [Test]
    public async Task GetPaymentByOrderIdAsync_ValidOrderId_ShouldReturnPayment()
    {
        var orderId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            CustomerId = Guid.NewGuid(),
            Amount = 100.50m,
            Currency = "USD",
            Status = PaymentStatus.Succeeded
        };

        _repositoryMock.Setup(x => x.GetByOrderIdAsync(orderId))
            .ReturnsAsync(payment);

        var result = await _paymentService.GetPaymentByOrderIdAsync(orderId);

        result.Should().NotBeNull();
        result?.OrderId.Should().Be(orderId);
        result?.Amount.Should().Be(100.50m);
        result?.Status.Should().Be(PaymentStatus.Succeeded);
    }

    [Test]
    public async Task RefundPaymentAsync_ValidRequest_ShouldReturnRefundedPayment()
    {
        var paymentId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = paymentId,
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 100.00m,
            Currency = "USD",
            Status = PaymentStatus.Succeeded,
            ExternalPaymentId = "ext_123456"
        };

        var refundRequest = new RefundRequestDto
        {
            PaymentId = paymentId,
            Amount = 50.00m,
            Reason = "Customer requested refund"
        };

        var refundResult = new PaymentProcessingResult
        {
            IsSuccess = true,
            ExternalPaymentId = "refund_123456",
            ProcessorResponse = new Dictionary<string, string>()
        };

        _repositoryMock.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync(payment);
        _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Payment>()))
            .ReturnsAsync(payment);
        _repositoryMock.Setup(x => x.AddEventAsync(It.IsAny<PaymentEvent>()))
            .Returns(Task.CompletedTask);
        _processorMock.Setup(x => x.RefundAsync(It.IsAny<Payment>(), It.IsAny<decimal>()))
            .ReturnsAsync(refundResult);

        var result = await _paymentService.RefundPaymentAsync(refundRequest);

        result.Should().NotBeNull();
        result?.Status.Should().Be(PaymentStatus.PartiallyRefunded);
        _processorMock.Verify(x => x.RefundAsync(It.IsAny<Payment>(), 50.00m), Times.Once);
    }

    [Test]
    public async Task RefundPaymentAsync_InvalidPaymentStatus_ShouldThrowInvalidOperationException()
    {
        var paymentId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = paymentId,
            Status = PaymentStatus.Failed
        };

        var refundRequest = new RefundRequestDto
        {
            PaymentId = paymentId,
            Amount = 50.00m,
            Reason = "Customer requested refund"
        };

        _repositoryMock.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync(payment);

        var act = async () => await _paymentService.RefundPaymentAsync(refundRequest);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cannot refund payment with status Failed");
    }

    [Test]
    public async Task RefundPaymentAsync_ExcessiveAmount_ShouldThrowInvalidOperationException()
    {
        var paymentId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = paymentId,
            Amount = 100.00m,
            Status = PaymentStatus.Succeeded
        };

        var refundRequest = new RefundRequestDto
        {
            PaymentId = paymentId,
            Amount = 150.00m,
            Reason = "Customer requested refund"
        };

        _repositoryMock.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync(payment);

        var act = async () => await _paymentService.RefundPaymentAsync(refundRequest);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Refund amount cannot exceed original payment amount");
    }

    [Test]
    public async Task CancelPaymentAsync_ValidPayment_ShouldReturnCancelledPayment()
    {
        var paymentId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = paymentId,
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 100.00m,
            Currency = "USD",
            Status = PaymentStatus.Pending,
            ExternalPaymentId = "ext_123456"
        };

        var cancelResult = new PaymentProcessingResult
        {
            IsSuccess = true,
            ProcessorResponse = new Dictionary<string, string>()
        };

        _repositoryMock.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync(payment);
        _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Payment>()))
            .ReturnsAsync(payment);
        _repositoryMock.Setup(x => x.AddEventAsync(It.IsAny<PaymentEvent>()))
            .Returns(Task.CompletedTask);
        _processorMock.Setup(x => x.CancelAsync(It.IsAny<Payment>()))
            .ReturnsAsync(cancelResult);

        var result = await _paymentService.CancelPaymentAsync(paymentId);

        result.Should().NotBeNull();
        result?.Status.Should().Be(PaymentStatus.Cancelled);
        _processorMock.Verify(x => x.CancelAsync(It.IsAny<Payment>()), Times.Once);
    }

    [Test]
    public async Task CancelPaymentAsync_InvalidPaymentStatus_ShouldThrowInvalidOperationException()
    {
        var paymentId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = paymentId,
            Status = PaymentStatus.Succeeded
        };

        _repositoryMock.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync(payment);

        var act = async () => await _paymentService.CancelPaymentAsync(paymentId);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Cannot cancel payment with status Succeeded");
    }

    [Test]
    public async Task UpdatePaymentStatusAsync_ValidRequest_ShouldReturnTrue()
    {
        var paymentId = Guid.NewGuid();
        var payment = new Payment
        {
            Id = paymentId,
            Status = PaymentStatus.Pending
        };

        _repositoryMock.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync(payment);
        _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Payment>()))
            .ReturnsAsync(payment);
        _repositoryMock.Setup(x => x.AddEventAsync(It.IsAny<PaymentEvent>()))
            .Returns(Task.CompletedTask);

        var result = await _paymentService.UpdatePaymentStatusAsync(paymentId, PaymentStatus.Succeeded, "Payment completed");

        result.Should().BeTrue();
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Payment>()), Times.Once);
    }

    [Test]
    public async Task UpdatePaymentStatusAsync_PaymentNotFound_ShouldReturnFalse()
    {
        var paymentId = Guid.NewGuid();

        _repositoryMock.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync((Payment?)null);

        var result = await _paymentService.UpdatePaymentStatusAsync(paymentId, PaymentStatus.Succeeded, "Payment completed");

        result.Should().BeFalse();
        _repositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Payment>()), Times.Never);
    }
}