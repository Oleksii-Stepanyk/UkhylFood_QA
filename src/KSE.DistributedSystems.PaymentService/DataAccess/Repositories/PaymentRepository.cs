using Microsoft.EntityFrameworkCore;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;
using KSE.DistributedSystems.PaymentService.DataAccess.Interfaces;

namespace KSE.DistributedSystems.PaymentService.DataAccess.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly PaymentDbContext _context;

    public PaymentRepository(PaymentDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Payment?> GetByIdAsync(Guid id)
    {
        return await _context.Payments
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Payment?> GetByOrderIdAsync(Guid orderId)
    {
        return await _context.Payments
            .FirstOrDefaultAsync(p => p.OrderId == orderId);
    }

    public async Task<IEnumerable<Payment>> GetByCustomerIdAsync(Guid customerId)
    {
        return await _context.Payments
            .Where(p => p.CustomerId == customerId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Payment>> GetByStatusAsync(PaymentStatus status)
    {
        return await _context.Payments
            .Where(p => p.Status == status)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<Payment> AddAsync(Payment payment)
    {
        var result = await _context.Payments.AddAsync(payment);
        await _context.SaveChangesAsync();
        return result.Entity;
    }

    public async Task<Payment?> UpdateAsync(Payment payment)
    {
        var existingPayment = await _context.Payments.FindAsync(payment.Id);
        if (existingPayment == null)
            return null;

        _context.Entry(existingPayment).CurrentValues.SetValues(payment);
        await _context.SaveChangesAsync();
        return existingPayment;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var payment = await _context.Payments.FindAsync(id);
        if (payment == null)
            return false;

        _context.Payments.Remove(payment);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<Payment>> GetPaymentHistoryAsync(Guid customerId, DateTime? from = null, DateTime? to = null)
    {
        var query = _context.Payments
            .Where(p => p.CustomerId == customerId);

        if (from.HasValue)
            query = query.Where(p => p.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(p => p.CreatedAt <= to.Value);

        return await query
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();
    }

    public async Task<Payment?> GetPaymentWithEventsAsync(Guid id)
    {
        return await _context.Payments
            .Include(p => p.Events)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task AddEventAsync(PaymentEvent paymentEvent)
    {
        await _context.PaymentEvents.AddAsync(paymentEvent);
        await _context.SaveChangesAsync();
    }
} 