using KSE.DistributedSystems.NotificationService.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;

namespace KSE.DistributedSystems.NotificationService.Services;
public class SendGridHealthCheck(IHttpClientFactory httpClientFactory, IOptions<SendGridSettings> options, ILogger<SendGridHealthCheck> logger) : IHealthCheck
{
    private readonly HttpClient _httpClient = httpClientFactory.CreateClient("HealthCheck");
    private readonly SendGridSettings _settings = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.sendgrid.com/v3/user/profile");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await _httpClient.SendAsync(request, cts.Token);
            stopwatch.Stop();

            var data = new Dictionary<string, object>
            {
                ["service"] = "SendGrid API",
                ["endpoint"] = "https://api.sendgrid.com/v3/user/profile",
                ["status"] = response.StatusCode.ToString(),
                ["responseTime"] = stopwatch.ElapsedMilliseconds
            };

            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy("SendGrid API is accessible and authenticated", data);
            }

            logger.LogWarning("SendGrid API health check failed with status {StatusCode}", response.StatusCode);
            return HealthCheckResult.Unhealthy($"SendGrid API returned {response.StatusCode}", null, data);
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("SendGrid API health check timed out");
            return HealthCheckResult.Unhealthy("SendGrid API health check timed out");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendGrid API health check failed");
            return HealthCheckResult.Unhealthy($"SendGrid API health check failed: {ex.Message}");
        }
    }
}