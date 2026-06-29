using KSE.DistributedSystems.OrderService.Models;

namespace KSE.DistributedSystems.OrderService.DataAccess.Repositories;

public interface IPaymentRepository
{
    Task<PaymentMethod?> GetPaymentMethodByCustomerId(Guid id);
}