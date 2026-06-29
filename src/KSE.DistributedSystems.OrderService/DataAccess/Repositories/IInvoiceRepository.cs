using KSE.DistributedSystems.OrderService.Models;

namespace KSE.DistributedSystems.OrderService.DataAccess.Repositories;

public interface IInvoiceRepository
{
    Task SaveInvoice(Invoice invoice);
    Task<Guid> UpdateInvoiceStatus(Guid orderId, PaymentStatus status);
}