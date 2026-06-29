using System;
using System.Collections.Generic;

namespace KSE.DistributedSystems.CustomerService.DataAccess.Models;

public record CustomerDTO(Guid Id, string? Name, string? Email, string? PhoneNumber, string? Address, uint LoyaltyPoints, IEnumerable<PaymentMethodDTO>? PaymentMethods)
{
    public CustomerDTO() : this(Guid.Empty, null, null, null, null, 0, null) { }
}