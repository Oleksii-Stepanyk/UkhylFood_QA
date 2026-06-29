using FluentValidation;
using KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;

namespace KSE.DistributedSystems.PaymentService.BusinessLogic.Validators;

public class PaymentRequestValidator : AbstractValidator<PaymentRequestDto>
{
    public PaymentRequestValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("Order ID is required");

        RuleFor(x => x.CustomerId)
            .NotEmpty().WithMessage("Customer ID is required");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount must be greater than 0")
            .LessThanOrEqualTo(10000).WithMessage("Amount cannot exceed $10,000");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Currency is required")
            .Length(3).WithMessage("Currency must be 3 characters")
            .Must(BeValidCurrency).WithMessage("Invalid currency code");

        RuleFor(x => x.PaymentMethod)
            .IsInEnum().WithMessage("Invalid payment method");

        When(x => x.PaymentMethod == PaymentMethod.CreditCard || x.PaymentMethod == PaymentMethod.DebitCard, () =>
        {
            RuleFor(x => x.CardDetails)
                .NotNull().WithMessage("Card details are required for card payments")
                .SetValidator(new PaymentCardValidator()!);
        });
    }

    private static bool BeValidCurrency(string currency)
    {
        var validCurrencies = new[] { "USD", "EUR", "GBP", "CAD", "AUD" };
        return validCurrencies.Contains(currency?.ToUpper());
    }
}

public class PaymentCardValidator : AbstractValidator<PaymentCardDto>
{
    public PaymentCardValidator()
    {
        RuleFor(x => x.CardNumber)
            .NotEmpty().WithMessage("Card number is required")
            .CreditCard().WithMessage("Invalid card number format");

        RuleFor(x => x.ExpiryMonth)
            .NotEmpty().WithMessage("Expiry month is required")
            .Matches(@"^(0[1-9]|1[0-2])$").WithMessage("Invalid expiry month format (MM)");

        RuleFor(x => x.ExpiryYear)
            .NotEmpty().WithMessage("Expiry year is required")
            .Matches(@"^\d{4}$").WithMessage("Invalid expiry year format (YYYY)")
            .Must(BeValidExpiryYear).WithMessage("Card has expired");

        RuleFor(x => x.Cvv)
            .NotEmpty().WithMessage("CVV is required")
            .Matches(@"^\d{3,4}$").WithMessage("CVV must be 3 or 4 digits");

        RuleFor(x => x.CardholderName)
            .NotEmpty().WithMessage("Cardholder name is required")
            .MinimumLength(2).WithMessage("Cardholder name must be at least 2 characters");
    }

    private static bool BeValidExpiryYear(string year)
    {
        if (!int.TryParse(year, out var yearInt))
            return false;

        return yearInt >= DateTime.Now.Year;
    }
} 