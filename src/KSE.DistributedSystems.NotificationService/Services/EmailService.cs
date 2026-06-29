using KSE.DistributedSystems.NotificationService.Models;
using KSE.DistributedSystems.Shared.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KSE.DistributedSystems.NotificationService.Services;

public class EmailService(HttpClient httpClient, IOptions<SendGridSettings> options, ILogger<EmailService> logger, ResiliencePolicies resiliencePolicies) : IEmailService
{
    private readonly SendGridSettings _settings = options.Value
        ?? throw new ArgumentNullException(nameof(options), "SendGrid settings cannot be null");
    private readonly ResiliencePipeline _httpPipeline = resiliencePolicies.CreateHttpClientPipeline("SendGrid");
    
    private static readonly ActivitySource ActivitySource = new("KSE.DistributedSystems.NotificationService.EmailService");

    public async Task SendEmailAsync(string to, string subject, string body)
    {
        using var activity = ActivitySource.StartActivity("EmailService.SendEmail");
        activity?.SetTag("email.to", to);
        activity?.SetTag("email.subject", subject);
        
        if (string.IsNullOrWhiteSpace(to))
            throw new ArgumentException("Email address cannot be empty", nameof(to));

        if (string.IsNullOrWhiteSpace(subject))
            throw new ArgumentException("Subject cannot be empty", nameof(subject));

        logger.LogInformation($"Sending email to: {to}, Subject: {subject}");

        try
        {
            await _httpPipeline.ExecuteAsync(async _ =>
            {
                using var sendActivity = ActivitySource.StartActivity("EmailService.SendEmail.SendGrid");
                sendActivity?.SetTag("sendgrid.api.endpoint", "/v3/mail/send");
                
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/v3/mail/send");
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

                var payload = new
                {
                    personalizations = new[]
                    {
                        new
                        {
                            to = new[] { new { email = to } },
                            subject
                        }
                    },
                    from = new { email = _settings.FromEmail },
                    content = new[]
                    {
                        new { type = "text/plain", value = body }
                    }
                };

                requestMessage.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await httpClient.SendAsync(requestMessage, _);
                
                sendActivity?.SetTag("http.status_code", (int)response.StatusCode);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync(_);
                    logger.LogError($"SendGrid API error: {response.StatusCode} - {errorContent}");
                    
                    sendActivity?.SetStatus(ActivityStatusCode.Error, $"SendGrid API error: {response.StatusCode}");
                    activity?.SetStatus(ActivityStatusCode.Error, $"Email send failed: {response.StatusCode}");

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        throw new HttpRequestException($"SendGrid rate limit exceeded: {response.StatusCode}");
                    }

                    if (response.StatusCode >= HttpStatusCode.InternalServerError)
                    {
                        throw new HttpRequestException($"SendGrid server error: {response.StatusCode}");
                    }

                    response.EnsureSuccessStatusCode();
                }

                logger.LogInformation($"Email sent successfully to {to}");
                activity?.SetTag("email.sent", "true");
                sendActivity?.SetStatus(ActivityStatusCode.Ok);
                
                return response;
            });
            
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            logger.LogError(ex, $"Failed to send email to {to} after all retry attempts");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }
    }
}