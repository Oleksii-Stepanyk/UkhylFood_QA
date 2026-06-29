using FluentAssertions;
using KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Validators;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;

namespace KSE.DistributedSystems.PaymentService.Tests;

public class ValidatorTests
{
    private PaymentRequestValidator _paymentRequestValidator;
    private RefundRequestValidator _refundRequestValidator;
    private PaymentCardValidator _paymentCardValidator;

    [SetUp]
    public void Setup()
    {
        _paymentRequestValidator = new PaymentRequestValidator();
        _refundRequestValidator = new RefundRequestValidator();
        _paymentCardValidator = new PaymentCardValidator();
    }

    [Test]
    public async Task PaymentRequestValidator_ValidRequest_ShouldPass()
    {
        var request = new PaymentRequestDto
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

        var result = await _paymentRequestValidator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task PaymentRequestValidator_InvalidAmount_ShouldFail()
    {
        var request = new PaymentRequestDto
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 0,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard
        };

        var result = await _paymentRequestValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Amount");
    }

    [Test]
    public async Task PaymentRequestValidator_ExcessiveAmount_ShouldFail()
    {
        var request = new PaymentRequestDto
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 15000m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard
        };

        var result = await _paymentRequestValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Amount" && e.ErrorMessage.Contains("cannot exceed"));
    }

    [Test]
    public async Task PaymentRequestValidator_InvalidCurrency_ShouldFail()
    {
        var request = new PaymentRequestDto
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 100.50m,
            Currency = "XYZ",
            PaymentMethod = PaymentMethod.CreditCard
        };

        var result = await _paymentRequestValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Currency");
    }

    [Test]
    public async Task PaymentRequestValidator_EmptyOrderId_ShouldFail()
    {
        var request = new PaymentRequestDto
        {
            OrderId = Guid.Empty,
            CustomerId = Guid.NewGuid(),
            Amount = 100.50m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard
        };

        var result = await _paymentRequestValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "OrderId");
    }

    [Test]
    public async Task PaymentRequestValidator_CreditCardWithoutDetails_ShouldFail()
    {
        var request = new PaymentRequestDto
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 100.50m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.CreditCard,
            CardDetails = null
        };

        var result = await _paymentRequestValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CardDetails");
    }

    [Test]
    public async Task PaymentCardValidator_ValidCard_ShouldPass()
    {
        var cardDto = new PaymentCardDto
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = "12",
            ExpiryYear = "2025",
            Cvv = "123",
            CardholderName = "John Doe"
        };

        var result = await _paymentCardValidator.ValidateAsync(cardDto);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task PaymentCardValidator_InvalidCardNumber_ShouldFail()
    {
        var cardDto = new PaymentCardDto
        {
            CardNumber = "1234567890123456",
            ExpiryMonth = "12",
            ExpiryYear = "2025",
            Cvv = "123",
            CardholderName = "John Doe"
        };

        var result = await _paymentCardValidator.ValidateAsync(cardDto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CardNumber");
    }

    [Test]
    public async Task PaymentCardValidator_InvalidExpiryMonth_ShouldFail()
    {
        var cardDto = new PaymentCardDto
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = "13",
            ExpiryYear = "2025",
            Cvv = "123",
            CardholderName = "John Doe"
        };

        var result = await _paymentCardValidator.ValidateAsync(cardDto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ExpiryMonth");
    }

    [Test]
    public async Task PaymentCardValidator_ExpiredCard_ShouldFail()
    {
        var cardDto = new PaymentCardDto
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = "12",
            ExpiryYear = "2020",
            Cvv = "123",
            CardholderName = "John Doe"
        };

        var result = await _paymentCardValidator.ValidateAsync(cardDto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ExpiryYear");
    }

    [Test]
    public async Task PaymentCardValidator_InvalidCvv_ShouldFail()
    {
        var cardDto = new PaymentCardDto
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = "12",
            ExpiryYear = "2025",
            Cvv = "12",
            CardholderName = "John Doe"
        };

        var result = await _paymentCardValidator.ValidateAsync(cardDto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Cvv");
    }

    [Test]
    public async Task PaymentCardValidator_ShortCardholderName_ShouldFail()
    {
        var cardDto = new PaymentCardDto
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = "12",
            ExpiryYear = "2025",
            Cvv = "123",
            CardholderName = "J"
        };

        var result = await _paymentCardValidator.ValidateAsync(cardDto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CardholderName");
    }

    [Test]
    public async Task RefundRequestValidator_ValidRequest_ShouldPass()
    {
        var request = new RefundRequestDto
        {
            PaymentId = Guid.NewGuid(),
            Amount = 50.00m,
            Reason = "Customer requested refund"
        };

        var result = await _refundRequestValidator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task RefundRequestValidator_EmptyPaymentId_ShouldFail()
    {
        var request = new RefundRequestDto
        {
            PaymentId = Guid.Empty,
            Amount = 50.00m,
            Reason = "Customer requested refund"
        };

        var result = await _refundRequestValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "PaymentId");
    }

    [Test]
    public async Task RefundRequestValidator_InvalidAmount_ShouldFail()
    {
        var request = new RefundRequestDto
        {
            PaymentId = Guid.NewGuid(),
            Amount = -10.00m,
            Reason = "Customer requested refund"
        };

        var result = await _refundRequestValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Amount");
    }

    [Test]
    public async Task RefundRequestValidator_LongReason_ShouldFail()
    {
        var request = new RefundRequestDto
        {
            PaymentId = Guid.NewGuid(),
            Amount = 50.00m,
            Reason = new string('x', 600)
        };

        var result = await _refundRequestValidator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Reason");
    }

    [Test]
    public async Task RefundRequestValidator_NullAmountAndReason_ShouldPass()
    {
        var request = new RefundRequestDto
        {
            PaymentId = Guid.NewGuid(),
            Amount = null,
            Reason = null
        };

        var result = await _refundRequestValidator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task PaymentRequestValidator_PayPalWithoutCardDetails_ShouldPass()
    {
        var request = new PaymentRequestDto
        {
            OrderId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            Amount = 100.50m,
            Currency = "USD",
            PaymentMethod = PaymentMethod.PayPal,
            CardDetails = null
        };

        var result = await _paymentRequestValidator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Test]
    public async Task PaymentCardValidator_EmptyCardNumber_ShouldFail()
    {
        var cardDto = new PaymentCardDto
        {
            CardNumber = "",
            ExpiryMonth = "12",
            ExpiryYear = "2025",
            Cvv = "123",
            CardholderName = "John Doe"
        };

        var result = await _paymentCardValidator.ValidateAsync(cardDto);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CardNumber");
    }

    [Test]
    public async Task PaymentCardValidator_FourDigitCvv_ShouldPass()
    {
        var cardDto = new PaymentCardDto
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = "12",
            ExpiryYear = "2025",
            Cvv = "1234",
            CardholderName = "John Doe"
        };

        var result = await _paymentCardValidator.ValidateAsync(cardDto);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
} 