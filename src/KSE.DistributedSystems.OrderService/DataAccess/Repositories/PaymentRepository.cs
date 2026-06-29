using KSE.DistributedSystems.OrderService.Models;

namespace KSE.DistributedSystems.OrderService.DataAccess.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly OrderDbContext _db;

    public PaymentRepository(OrderDbContext db)
    {
        _db = db;
    }

    public async Task<PaymentMethod?> GetPaymentMethodByCustomerId(Guid id) =>
        await _db.PaymentMethods.FindAsync(id);
    
}