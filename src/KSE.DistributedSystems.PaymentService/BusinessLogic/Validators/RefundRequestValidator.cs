using FluentValidation;
using KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;

namespace KSE.DistributedSystems.PaymentService.BusinessLogic.Validators;

public class RefundRequestValidator : AbstractValidator<RefundRequestDto>
{
    public RefundRequestValidator()
    {
        RuleFor(x => x.PaymentId)
            .NotEmpty().WithMessage("Payment ID is required");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Refund amount must be greater than 0")
            .When(x => x.Amount.HasValue);

        RuleFor(x => x.Reason)
            .MaximumLength(500).WithMessage("Reason cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Reason));
    }
}
