using KSE.DistributedSystems.CustomerService.DataAccess.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KSE.DistributedSystems.CustomerService.DataAccess.Repositories;

public interface ICustomerRepository
{
    Task<CustomerDTO?> GetByIdAsync(Guid id);
    Task<IEnumerable<CustomerDTO>> GetAllAsync();
    Task<CustomerDTO> AddAsync(CustomerDTO customer);
    Task<CustomerDTO> UpdateAsync(CustomerDTO customer);
    Task<bool> DeleteAsync(Guid id);
}