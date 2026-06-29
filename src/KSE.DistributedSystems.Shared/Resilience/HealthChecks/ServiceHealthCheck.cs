using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using System.Net.NetworkInformation;

namespace KSE.DistributedSystems.Shared.Resilience.HealthChecks;

public class ServiceHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;
    private readonly string _serviceUrl;
    private readonly string _serviceName;
    private readonly ILogger<ServiceHealthCheck> _logger;

    public ServiceHealthCheck(HttpClient httpClient, string serviceUrl, string serviceName, ILogger<ServiceHealthCheck> logger)
    {
        _httpClient = httpClient;
        _serviceUrl = serviceUrl;
        _serviceName = serviceName;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await _httpClient.GetAsync($"{_serviceUrl}/health", cts.Token);

            if (response.IsSuccessStatusCode)
            {
                var responseTime = response.Headers.Date?.Subtract(DateTime.UtcNow).TotalMilliseconds ?? 0;
                var data = new Dictionary<string, object>
                {
                    ["service"] = _serviceName,
                    ["url"] = _serviceUrl,
                    ["status"] = response.StatusCode.ToString(),
                    ["responseTime"] = Math.Abs(responseTime)
                };

                return HealthCheckResult.Healthy($"{_serviceName} is healthy", data);
            }

            return HealthCheckResult.Unhealthy($"{_serviceName} returned {response.StatusCode}");
        }
        catch (TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy($"{_serviceName} health check timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for {ServiceName}", _serviceName);
            return HealthCheckResult.Unhealthy($"{_serviceName} health check failed: {ex.Message}");
        }
    }
}

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly Func<Task<bool>> _databaseConnectionTest;
    private readonly string _databaseName;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(Func<Task<bool>> databaseConnectionTest, string databaseName, ILogger<DatabaseHealthCheck> logger)
    {
        _databaseConnectionTest = databaseConnectionTest;
        _databaseName = databaseName;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var isHealthy = await _databaseConnectionTest();

            if (isHealthy)
            {
                return HealthCheckResult.Healthy($"{_databaseName} database is healthy");
            }

            return HealthCheckResult.Unhealthy($"{_databaseName} database is not responding");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed for {DatabaseName}", _databaseName);
            return HealthCheckResult.Unhealthy($"{_databaseName} database health check failed: {ex.Message}");
        }
    }
} 