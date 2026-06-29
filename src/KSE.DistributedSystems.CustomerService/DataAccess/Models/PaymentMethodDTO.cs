using System;

namespace KSE.DistributedSystems.CustomerService.DataAccess.Models;

public record PaymentMethodDTO(Guid Id, Guid CustomerId, string? Type)
{
    public PaymentMethodDTO() : this(Guid.Empty, Guid.Empty, null) { }
}