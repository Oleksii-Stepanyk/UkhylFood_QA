using System;
using System.Threading.Tasks;

namespace KSE.DistributedSystems.CustomerService.Services;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);
}