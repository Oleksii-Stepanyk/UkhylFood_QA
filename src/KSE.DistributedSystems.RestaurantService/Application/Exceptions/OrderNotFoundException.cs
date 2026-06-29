using System;

namespace KSE.DistributedSystems.RestaurantService.Application.Exceptions;

public class OrderNotFoundException(Guid id) : StatusCodeBasedExceptions.NotFoundException
{
    public override string Message { get; } = $"Order with id {id} was not found";
}