using System;

namespace KSE.DistributedSystems.RestaurantService.Application.Exceptions;

public static class StatusCodeBasedExceptions
{
    public class NotFoundException : Exception
    {
        
    }

    public class BadRequestException : Exception
    {
        
    }
}