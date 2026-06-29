using KSE.DistributedSystems.CustomerService.DataAccess.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KSE.DistributedSystems.CustomerService.DataAccess.Repositories;

public interface IPaymentMethodRepository
{
    Task<PaymentMethod?> GetByIdAsync(Guid id);
    Task<IEnumerable<PaymentMethod>> GetByCustomerIdAsync(Guid customerId);
    Task<PaymentMethod> AddAsync(PaymentMethod paymentMethod);
    Task<PaymentMethod> UpdateAsync(PaymentMethod paymentMethod);
    Task<bool> DeleteAsync(Guid id);
}