using KSE.DistributedSystems.CustomerService.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KSE.DistributedSystems.CustomerService.DataAccess.Repositories;

public class PaymentMethodRepository(CustomerDbContext context) : IPaymentMethodRepository
{
    public async Task<PaymentMethod?> GetByIdAsync(Guid id) =>
        await context.PaymentMethods.Include(pm => pm.Customer).FirstOrDefaultAsync(pm => pm.Id == id);

    public async Task<IEnumerable<PaymentMethod>> GetByCustomerIdAsync(Guid customerId) =>
        await context.PaymentMethods.Where(pm => pm.CustomerId == customerId).ToListAsync();

    public async Task<PaymentMethod> AddAsync(PaymentMethod paymentMethod)
    {
        context.PaymentMethods.Add(paymentMethod);
        await context.SaveChangesAsync();
        return paymentMethod;
    }

    public async Task<PaymentMethod> UpdateAsync(PaymentMethod paymentMethod)
    {
        context.PaymentMethods.Update(paymentMethod);
        await context.SaveChangesAsync();
        return paymentMethod;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var paymentMethod = await context.PaymentMethods.FindAsync(id);
        if (paymentMethod == null) return false;
        context.PaymentMethods.Remove(paymentMethod);
        return await context.SaveChangesAsync() > 0;
    }
}