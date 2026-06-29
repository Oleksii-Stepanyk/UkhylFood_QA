using KSE.DistributedSystems.NotificationService.Models;
using KSE.DistributedSystems.NotificationService.Services;
using KSE.DistributedSystems.Shared.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using System.Text.Json;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace KSE.DistributedSystems.NotificationService.Tests;

[Trait("Category", "Integration")]
public class EmailServiceIntegrationTests()
{
    private const string TestApiKey = "test-api-key-123";
    private const string TestFromEmail = "test@example.com";

    [Fact]
    public async Task SendEmailAsync_WithValidData_ShouldSendRequestAndHandleResponse()
    {
        using var mockServer = WireMockServer.Start();
        using var httpClient = new HttpClient { BaseAddress = new Uri(mockServer.Url ?? throw new InvalidOperationException("Mock server URL is null")) };

        var loggerMock = new Mock<ILogger<EmailService>>();

        var resilienceOptions = Options.Create(new ResilienceOptions());

        var resilienceLogger = new Mock<ILogger<ResiliencePolicies>>().Object;
        var resiliencePolicies = new ResiliencePolicies(resilienceOptions, resilienceLogger);

        var sendGridSettings = new SendGridSettings
        {
            ApiKey = TestApiKey,
            FromEmail = TestFromEmail
        };
        var options = Options.Create(sendGridSettings);
        var emailService = new EmailService(httpClient, options, loggerMock.Object, resiliencePolicies);

        const string toEmail = "recipient@example.com";
        const string subject = "Integration Test Subject";
        const string body = "Integration test email body content";

        // setup wiremock like sendgrid API
        mockServer
            .Given(Request.Create()
                .WithPath("/v3/mail/send")
                .UsingPost()
                .WithHeader("Authorization", $"Bearer {TestApiKey}")
                .WithHeader("Content-Type", "application/json"))
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.Accepted)
                .WithHeader("Content-Type", "application/json")
                .WithBody("{\"message\":\"success\"}"));

        await emailService.SendEmailAsync(toEmail, subject, body);

        var requests = mockServer.LogEntries.ToList();
        Assert.NotEmpty(requests);

        var request = requests.First()!;
        Assert.Equal("/v3/mail/send", request.RequestMessage.Path);
        Assert.Equal("POST", request.RequestMessage.Method);
        Assert.Contains($"Bearer {TestApiKey}", request.RequestMessage.Headers!["Authorization"]);

        var requestBody = request.RequestMessage.Body;
        Assert.NotNull(requestBody);

        var payloadJson = JsonSerializer.Deserialize<JsonElement>(requestBody);
        var personalizations = payloadJson.GetProperty("personalizations")[0];
        var toArray = personalizations.GetProperty("to")[0];

        Assert.Equal(toEmail, toArray.GetProperty("email").GetString());
        Assert.Equal(subject, personalizations.GetProperty("subject").GetString());
        Assert.Equal(TestFromEmail, payloadJson.GetProperty("from").GetProperty("email").GetString());
        Assert.Equal(body, payloadJson.GetProperty("content")[0].GetProperty("value").GetString());
    }

    [Fact]
    public async Task EmailService_WithResiliencePolicies_ShouldRetryAndSucceedAfterFailures()
    {
        using var mockServer = WireMockServer.Start();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.Configure<SendGridSettings>(options =>
                {
                    options.ApiKey = TestApiKey;
                    options.FromEmail = TestFromEmail;
                });

                services.Configure<ResilienceOptions>(options =>
                {
                    options.Retry.MaxRetryAttempts = 3;
                    options.Retry.BaseDelay = TimeSpan.FromMilliseconds(100);
                    options.CircuitBreaker.HandledEventsAllowedBeforeBreaking = 2;
                    options.CircuitBreaker.DurationOfBreak = TimeSpan.FromSeconds(5);
                    options.Timeout.ExternalServiceTimeout = TimeSpan.FromSeconds(2);
                });
                services.AddSingleton<ResiliencePolicies>();

                services.AddHttpClient<IEmailService, EmailService>("SendGrid", client =>
                {
                    client.BaseAddress = new Uri(mockServer.Url ?? throw new InvalidOperationException("Mock server URL is null"));
                    client.Timeout = TimeSpan.FromSeconds(5);
                });
                services.AddLogging(builder => builder.AddConsole());
            })
            .Build();

        const string toEmail = "resilience-test@example.com";
        const string subject = "Resilience Test";
        const string body = "Testing resilience policies: retry, circuit breaker, and timeout";

        // wiremock will fail first 2 requests and succeed on 3rd
        mockServer
            .Given(Request.Create()
                .WithPath("/v3/mail/send")
                .UsingPost())
            .InScenario("RetryTest")
            .WillSetStateTo("FirstCall")
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.InternalServerError)
                .WithBody("Server temporarily unavailable"));

        mockServer
            .Given(Request.Create()
                .WithPath("/v3/mail/send")
                .UsingPost())
            .InScenario("RetryTest")
            .WhenStateIs("FirstCall")
            .WillSetStateTo("SecondCall")
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.InternalServerError)
                .WithBody("Server temporarily unavailable"));

        mockServer
            .Given(Request.Create()
                .WithPath("/v3/mail/send")
                .UsingPost())
            .InScenario("RetryTest")
            .WhenStateIs("SecondCall")
            .WillSetStateTo("Success")
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.Accepted)
                .WithBody("{\"message\":\"success\"}"));

        var emailService = host.Services.GetRequiredService<IEmailService>();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // resilience should handle the transient failures and succeed after some retries
        await emailService.SendEmailAsync(toEmail, subject, body);

        stopwatch.Stop();
        var requests = mockServer.LogEntries.ToList();

        // at least 2 attempts according to retry policy
        Assert.True(requests.Count >= 2, $"Expected at least 2 requests due to retry policy, but got {requests.Count}");
        Assert.True(requests.Count <= 4, $"Expected at most 4 requests (initial + 3 retries), but got {requests.Count}");

        // all requests went to the correct endpoint
        Assert.All(requests, request =>
        {
            Assert.Equal("/v3/mail/send", request.RequestMessage.Path);
            Assert.Equal("POST", request.RequestMessage.Method);
        });

        // if too long test fail
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Operation took too long: {stopwatch.Elapsed.TotalSeconds} seconds");
    }
}