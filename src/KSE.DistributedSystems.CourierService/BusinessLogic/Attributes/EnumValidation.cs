using System;
using System.ComponentModel.DataAnnotations;

namespace KSE.DistributedSystems.CourierService.BusinessLogic.Attributes;

public class EnumValidation : ValidationAttribute
{
    private readonly Type _enumType;

    public EnumValidation(Type enumType)
    {
        _enumType = enumType;
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
            return ValidationResult.Success;

        if (!_enumType.IsEnum)
            throw new ArgumentException("EnumValidation can only be used with enum types.");

        if (value is string stringValue)
        {
            if (Enum.TryParse(_enumType, stringValue, ignoreCase: true, out _))
                return ValidationResult.Success;
        }

        return new ValidationResult($"Invalid value '{value}' for enum type '{_enumType.Name}'.");
    }
}