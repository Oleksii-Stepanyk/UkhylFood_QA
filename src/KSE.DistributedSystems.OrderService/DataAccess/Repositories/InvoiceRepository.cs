using KSE.DistributedSystems.OrderService.Models;
using Microsoft.EntityFrameworkCore;

namespace KSE.DistributedSystems.OrderService.DataAccess.Repositories;

public class InvoiceRepository : IInvoiceRepository
{
    private readonly OrderDbContext _db;

    public InvoiceRepository(OrderDbContext db)
    {
        _db = db;
    }

    public async Task SaveInvoice(Invoice invoice)
    {
        await _db.Invoices.AddAsync(invoice);
        await _db.SaveChangesAsync();
    }

    public async Task<Guid> UpdateInvoiceStatus(Guid orderId, PaymentStatus status)
    {
        var invoice = await _db.Invoices.FirstOrDefaultAsync(i => i.OrderId == orderId);
        if (invoice != null)
        {
            invoice.PaymentStatus = status;
            _db.Invoices.Update(invoice);
        }

        await _db.SaveChangesAsync();

        return orderId;
    }
}