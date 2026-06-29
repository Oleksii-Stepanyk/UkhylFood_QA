using KSE.DistributedSystems.CustomerService.DataAccess.Models;
using System;
using System.Threading.Tasks;

namespace KSE.DistributedSystems.CustomerService.Services;

public interface ICustomerService
{
    Task<CustomerDTO?> GetCustomerAsync(Guid id);
    Task UpdateCustomerAsync(Guid id, CustomerDTO customer);
}