using KSE.DistributedSystems.CustomerService.DataAccess.Models;
using KSE.DistributedSystems.CustomerService.DataAccess.Repositories;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MassTransit.Internals.Caching;
using Microsoft.Extensions.Logging;

namespace KSE.DistributedSystems.CustomerService.Services;

public class CustomerService : ICustomerService
{
    private readonly ICustomerRepository _repository;
    private readonly ICacheService _cacheService;
    private readonly ILogger<CustomerService> _logger;
    private readonly CustomerMonitoringService _monitoring;
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromMinutes(5);

    public CustomerService(ICustomerRepository customerRepository, ICacheService cacheService, ILogger<CustomerService> logger, CustomerMonitoringService monitoring)
    {
        _repository = customerRepository;
        _cacheService = cacheService;
        _logger = logger;
        _monitoring = monitoring;
    }
    
    public async Task<CustomerDTO?> GetCustomerAsync(Guid id)
    {
        var stopwatch = Stopwatch.StartNew();
        var success = false;
        
        try
        {
            var cacheKey = $"customer:{id}";
            _logger.LogDebug("Retrieving from cache by key {Key}", cacheKey);

            var cached = await _cacheService.GetAsync<CustomerDTO>(cacheKey);
            if (cached != null)
            {
                _logger.LogInformation("Retrieved from cache successfully");
                _logger.LogDebug("Retrieved entity {@Entity}", cached);
                return cached;
            }

            _logger.LogInformation("No results were present in cache, retrieving from database");
            var customer = await _repository.GetByIdAsync(id);
            if (customer != null)
            {
                _logger.LogInformation("Retrieval successful. Saving into cache");
                await _cacheService.SetAsync(cacheKey, customer, CacheExpiry);
            }

            success = true;
            _logger.LogDebug("Retrieved entity {@Entity}", customer);
            return customer;
        }
        finally
        {
            stopwatch.Stop();
            _monitoring.RecordCustomerProcessingDuration("get_by_id", stopwatch.ElapsedMilliseconds, success);
        }
    }

    public async Task UpdateCustomerAsync(Guid id, CustomerDTO customer)
    {
        var stopwatch = Stopwatch.StartNew();
        var success = false;

        try
        {
            _logger.LogInformation("Updating customer with id {Id}", id);
            await _repository.UpdateAsync(customer);
            var cacheKey = $"customer:{id}";
            _logger.LogInformation("Caching customer with id {Id}", id);
            await _cacheService.SetAsync(cacheKey, customer, CacheExpiry);
            
            success = true;
        }
        finally
        {
            stopwatch.Stop();
            _monitoring.RecordCustomerProcessingDuration("update", stopwatch.ElapsedMilliseconds, success);
        }
    }
}