using KSE.DistributedSystems.PaymentService.DataAccess.Entities;

namespace KSE.DistributedSystems.PaymentService.DataAccess.Interfaces;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Guid id);
    Task<Payment?> GetByOrderIdAsync(Guid orderId);
    Task<IEnumerable<Payment>> GetByCustomerIdAsync(Guid customerId);
    Task<IEnumerable<Payment>> GetByStatusAsync(PaymentStatus status);
    Task<Payment> AddAsync(Payment payment);
    Task<Payment?> UpdateAsync(Payment payment);
    Task<bool> DeleteAsync(Guid id);
    Task<IEnumerable<Payment>> GetPaymentHistoryAsync(Guid customerId, DateTime? from = null, DateTime? to = null);
    Task<Payment?> GetPaymentWithEventsAsync(Guid id);
    Task AddEventAsync(PaymentEvent paymentEvent);
} 