using AutoMapper;
using KSE.DistributedSystems.CustomerService.DataAccess.Entities;
using KSE.DistributedSystems.CustomerService.DataAccess.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KSE.DistributedSystems.CustomerService.DataAccess.Repositories;

public class CustomerRepository : ICustomerRepository
{
    private readonly CustomerDbContext _context;
    private readonly IMapper _mapper;

    public CustomerRepository(CustomerDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<CustomerDTO?> GetByIdAsync(Guid id) =>
        await _context.Customers.Include(c => c.PaymentMethods)
            .Where(c => c.Id == id)
            .Select(c => _mapper.Map<CustomerDTO>(c))
            .FirstOrDefaultAsync();

    public async Task<IEnumerable<CustomerDTO>> GetAllAsync() =>
        await _context.Customers.Include(c => c.PaymentMethods)
            .Select(c => _mapper.Map<CustomerDTO>(c))
            .ToListAsync();

    public async Task<CustomerDTO> AddAsync(CustomerDTO customerDto)
    {
        var customer = _mapper.Map<Customer>(customerDto);
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();
        return _mapper.Map<CustomerDTO>(customer);
    }

    public async Task<CustomerDTO> UpdateAsync(CustomerDTO customerDto)
    {
        var customer = await _context.Customers.Include(c => c.PaymentMethods).FirstOrDefaultAsync(c => c.Id == customerDto.Id)
            ?? throw new InvalidOperationException("Customer not found");
        _mapper.Map(customerDto, customer);
        await _context.SaveChangesAsync();
        return _mapper.Map<CustomerDTO>(customer);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var customer = await _context.Customers.FindAsync(id);
        if (customer == null) return false;
        _context.Customers.Remove(customer);
        return await _context.SaveChangesAsync() > 0;
    }
}