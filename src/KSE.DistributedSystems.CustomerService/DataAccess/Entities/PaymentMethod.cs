using System;

namespace KSE.DistributedSystems.CustomerService.DataAccess.Entities;

public class PaymentMethod
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public PaymentMethodType Type { get; set; }
    public required string Details { get; set; }
    public bool IsDefault { get; set; }
    public required Customer Customer { get; set; }
}

public enum PaymentMethodType
{
    Card,
    ApplePay,
    GooglePay,
    Cash
}