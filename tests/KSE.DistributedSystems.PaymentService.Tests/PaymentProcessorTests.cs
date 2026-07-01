using FluentAssertions;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Services;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Concurrent;

namespace KSE.DistributedSystems.PaymentService.Tests;

public class PaymentProcessorTests
{
    private Mock<ILogger<PaymentProcessor>> _loggerMock;
    private PaymentProcessor _paymentProcessor;

    [SetUp]
    public void Setup()
    {
        _loggerMock = new Mock<ILogger<PaymentProcessor>>();
        _paymentProcessor = new PaymentProcessor(_loggerMock.Object);
    }

    [Fact]
    public async Task ProcessAsync_SmallAmount_ShouldSucceed()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Amount = 50.00m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard
        };

        var result = await _paymentProcessor.ProcessAsync(payment);

        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.ExternalPaymentId.Should().NotBeNullOrEmpty();
        result.ProcessorResponse.Should().ContainKey("transaction_id");
        result.ProcessorResponse.Should().ContainKey("processor");
        result.ProcessorResponse.Should().ContainKey("authorization_code");
    }

    [Fact]
    public async Task ProcessAsync_VerySmallAmount_ShouldFail()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Amount = 0.50m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard
        };

        var result = await _paymentProcessor.ProcessAsync(payment);

        result.Should().NotBeNull();
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Be("Amount too small for processing");
    }

    [Fact]
    public async Task RefundAsync_ValidPayment_ShouldSucceed()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Amount = 100.00m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard,
            ExternalPaymentId = "ext_123456"
        };

        PaymentProcessingResult? result = null;
        var maxAttempts = 50;
        var attemptCount = 0;

        while (attemptCount < maxAttempts)
        {
            result = await _paymentProcessor.RefundAsync(payment, 50.00m);
            if (result.IsSuccess)
                break;
            attemptCount++;
        }

        result.Should().NotBeNull();
        result!.IsSuccess.Should().BeTrue($"Expected refund to succeed within {maxAttempts} attempts, but failed {attemptCount + 1} times");
        result.ExternalPaymentId.Should().NotBeNullOrEmpty();
        result.ProcessorResponse.Should().ContainKey("refund_id");
    }

    [Fact]
    public async Task RefundAsync_NullAmount_ShouldRefundFullAmount()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Amount = 100.00m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard,
            ExternalPaymentId = "ext_123456"
        };

        var result = await _paymentProcessor.RefundAsync(payment, null);

        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.ProcessorResponse.Should().ContainKey("refund_id");
    }

    [Fact]
    public async Task CancelAsync_ValidPayment_ShouldSucceed()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Amount = 100.00m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard,
            ExternalPaymentId = "ext_123456"
        };

        var result = await _paymentProcessor.CancelAsync(payment);

        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.ProcessorResponse.Should().ContainKey("cancellation_reason");
    }

    [Fact]
    public async Task ProcessAsync_HighAmount_ShouldHaveVariableSuccess()
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            Amount = 6000.00m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard
        };

        var tasks = Enumerable.Range(0, 10)
            .Select(async _ => await _paymentProcessor.ProcessAsync(payment))
            .ToArray();
        
        var results = await Task.WhenAll(tasks);

        var successCount = results.Count(r => r.IsSuccess);
        var failureCount = results.Count(r => !r.IsSuccess);

        successCount.Should().BeGreaterThan(0);
        failureCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ProcessAsync_NormalAmount_ShouldMostlySucceed()
    {
        var payments = new List<Payment>();
        for (int i = 0; i < 100; i++)
        {
            payments.Add(new Payment
            {
                Id = Guid.NewGuid(),
                Amount = 100.00m,
                PaymentMethod = PaymentMethod.CreditCard
            });
        }

        var tasks = payments.Select(async payment => 
            await _paymentProcessor.ProcessAsync(payment)).ToArray();
        
        var results = await Task.WhenAll(tasks);

        var successfulResults = results.Count(r => r.IsSuccess);
        var failureRate = (double)(results.Length - successfulResults) / results.Length;

        successfulResults.Should().BeGreaterThan(80);
        failureRate.Should().BeLessOrEqualTo(0.15);
    }

    [Fact]
    public async Task ProcessAsync_DifferentPaymentMethods_ShouldReturnCorrectProcessor()
    {
        var creditCardPayment = new Payment
        {
            Id = Guid.NewGuid(),
            Amount = 100.00m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard
        };

        var payPalPayment = new Payment
        {
            Id = Guid.NewGuid(),
            Amount = 100.00m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.PayPal
        };

        var creditCardResult = await _paymentProcessor.ProcessAsync(creditCardPayment);
        var payPalResult = await _paymentProcessor.ProcessAsync(payPalPayment);

        creditCardResult.ProcessorResponse.Should().ContainKey("processor");
        payPalResult.ProcessorResponse.Should().ContainKey("processor");
        
        if (creditCardResult.IsSuccess)
        {
            creditCardResult.ProcessorResponse["processor"].Should().Be("Stripe");
        }
        
        if (payPalResult.IsSuccess)
        {
            payPalResult.ProcessorResponse["processor"].Should().Be("PayPal");
        }
    }
}