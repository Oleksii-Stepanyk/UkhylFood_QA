namespace KSE.DistributedSystems.RestaurantService.Application.Exceptions;

public class UnallowedValueException(string providedValue) : StatusCodeBasedExceptions.BadRequestException
{
    public override string Message { get; } = $"Provided value {providedValue} was not allowed";
}