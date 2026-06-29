using System.ComponentModel.DataAnnotations;

namespace KSE.DistributedSystems.OrderService.Models;

public class PaymentMethod
{
    public enum Type
    {
        Card,
        Wallet,
        Cash
    }
    
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Type MethodType { get; set; }
    public string? Details { get; set; }
    public bool IsDefault { get; set; }

    public List<Invoice> Invoices { get; set; } = [];
}