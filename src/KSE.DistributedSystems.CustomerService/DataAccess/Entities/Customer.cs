using System;
using System.Collections.Generic;

namespace KSE.DistributedSystems.CustomerService.DataAccess.Entities;
public class Customer
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string PhoneNumber { get; set; }
    public required string Address { get; set; }
    public List<PaymentMethod> PaymentMethods { get; set; } = [];
    public uint LoyaltyPoints { get; set; }
}