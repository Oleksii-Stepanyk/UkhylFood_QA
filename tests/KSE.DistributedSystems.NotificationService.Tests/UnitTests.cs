using KSE.DistributedSystems.NotificationService.Models;
using KSE.DistributedSystems.NotificationService.Services;
using KSE.DistributedSystems.Shared.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;
using Xunit;

namespace KSE.DistributedSystems.NotificationService.Tests;

[Trait("Category", "Unit")]
public class UnitTests
{
    private readonly Mock<ILogger<EmailService>> _loggerMock;
    private readonly ResiliencePolicies _resiliencePolicies;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly IOptions<SendGridSettings> _settings;

    public UnitTests()
    {
        _loggerMock = new Mock<ILogger<EmailService>>();

        var resilienceOptions = Options.Create(new ResilienceOptions
        {
            // minimum retries for testing
            Retry = new RetryOptions
            {
                MaxRetryAttempts = 1, 
                BaseDelay = TimeSpan.FromMilliseconds(1)
            },
            Timeout = new TimeoutOptions
            {
                ExternalServiceTimeout = TimeSpan.FromSeconds(30)
            }
        });
        var resilienceLogger = new Mock<ILogger<ResiliencePolicies>>().Object;
        _resiliencePolicies = new ResiliencePolicies(resilienceOptions, resilienceLogger);

        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("https://api.sendgrid.com")
        };

        _settings = Options.Create(new SendGridSettings
        {
            ApiKey = "test-api-key",
            FromEmail = "test@example.com"
        });
    }

    [Fact]
    public async Task SendEmailAsync_WithInvalidToEmail_ShouldThrowArgumentException()
    {
        var emailService = new EmailService(_httpClient, _settings, _loggerMock.Object, _resiliencePolicies);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            emailService.SendEmailAsync("", "Valid Subject", "Valid Body"));

        Assert.Equal("to", exception.ParamName);
        Assert.Contains("Email address cannot be empty", exception.Message);
    }

    [Fact]
    public async Task SendEmailAsync_WithNullToEmail_ShouldThrowArgumentException()
    {
        var emailService = new EmailService(_httpClient, _settings, _loggerMock.Object, _resiliencePolicies);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            emailService.SendEmailAsync(null!, "Valid Subject", "Valid Body"));

        Assert.Equal("to", exception.ParamName);
        Assert.Contains("Email address cannot be empty", exception.Message);
    }

    [Fact]
    public async Task SendEmailAsync_WithInvalidSubject_ShouldThrowArgumentException()
    {
        var emailService = new EmailService(_httpClient, _settings, _loggerMock.Object, _resiliencePolicies);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            emailService.SendEmailAsync("valid@email.com", "   ", "Valid Body"));

        Assert.Equal("subject", exception.ParamName);
        Assert.Contains("Subject cannot be empty", exception.Message);
    }

    [Fact]
    public async Task SendEmailAsync_WithNullSubject_ShouldThrowArgumentException()
    {
        var emailService = new EmailService(_httpClient, _settings, _loggerMock.Object, _resiliencePolicies);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            emailService.SendEmailAsync("valid@email.com", null!, "Valid Body"));

        Assert.Equal("subject", exception.ParamName);
        Assert.Contains("Subject cannot be empty", exception.Message);
    }

    [Fact]
    public async Task SendEmailAsync_WithValidInputs_ShouldCreateCorrectHttpRequest()
    {
        const string toEmail = "recipient@test.com";
        const string subject = "Test Subject";
        const string body = "Test Body Content";

        var responseMessage = new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("{\"message\":\"success\"}")
        };

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        var emailService = new EmailService(_httpClient, _settings, _loggerMock.Object, _resiliencePolicies);

        await emailService.SendEmailAsync(toEmail, subject, body);

        _httpMessageHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains("/v3/mail/send") &&
                    req.Headers.Authorization!.Scheme == "Bearer" &&
                    req.Headers.Authorization.Parameter == "test-api-key" &&
                    req.Content!.Headers.ContentType!.MediaType == "application/json"
                ),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendEmailAsync_WithValidInputs_ShouldCreateCorrectPayload()
    {
        const string toEmail = "recipient@test.com";
        const string subject = "Test Subject";
        const string body = "Test Body Content";
        string capturedPayload = string.Empty;

        var responseMessage = new HttpResponseMessage(HttpStatusCode.Accepted);

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedPayload = await req.Content!.ReadAsStringAsync(_);
            })
            .ReturnsAsync(responseMessage);

        var emailService = new EmailService(_httpClient, _settings, _loggerMock.Object, _resiliencePolicies);

        await emailService.SendEmailAsync(toEmail, subject, body);

        var payloadJson = JsonSerializer.Deserialize<JsonElement>(capturedPayload);

        var personalizations = payloadJson.GetProperty("personalizations")[0];
        var toArray = personalizations.GetProperty("to")[0];

        Assert.Equal(toEmail, toArray.GetProperty("email").GetString());
        Assert.Equal(subject, personalizations.GetProperty("subject").GetString());
        Assert.Equal("test@example.com", payloadJson.GetProperty("from").GetProperty("email").GetString());
        Assert.Equal(body, payloadJson.GetProperty("content")[0].GetProperty("value").GetString());
        Assert.Equal("text/plain", payloadJson.GetProperty("content")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task SendEmailAsync_WhenSuccessful_ShouldLogCorrectMessages()
    {
        const string toEmail = "recipient@test.com";
        const string subject = "Test Subject";
        const string body = "Test Body Content";

        var responseMessage = new HttpResponseMessage(HttpStatusCode.Accepted);

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        var emailService = new EmailService(_httpClient, _settings, _loggerMock.Object, _resiliencePolicies);

        await emailService.SendEmailAsync(toEmail, subject, body);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Sending email to: {toEmail}, Subject: {subject}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Email sent successfully to {toEmail}")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendEmailAsync_WithTooManyRequestsError_ShouldLogError()
    {
        const string toEmail = "recipient@test.com";
        const string subject = "Test Subject";
        const string body = "Test Body Content";
        const string errorContent = "Rate limit exceeded";

        var responseMessage = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(errorContent)
        };

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        var emailService = new EmailService(_httpClient, _settings, _loggerMock.Object, _resiliencePolicies);

        await emailService.SendEmailAsync(toEmail, subject, body);

        // error logged but not thrown
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SendGrid API error: TooManyRequests")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task SendEmailAsync_WithServerError_ShouldLogError()
    {
        const string toEmail = "recipient@test.com";
        const string subject = "Test Subject";
        const string body = "Test Body Content";
        const string errorContent = "Internal server error";

        var responseMessage = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(errorContent)
        };

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        var emailService = new EmailService(_httpClient, _settings, _loggerMock.Object, _resiliencePolicies);

        await emailService.SendEmailAsync(toEmail, subject, body);

        // error logged but not thrown
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SendGrid API error: InternalServerError")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task SendEmailAsync_WithHttpRequestException_ShouldLogFailureMessage()
    {
        const string toEmail = "recipient@test.com";
        const string subject = "Test Subject";
        const string body = "Test Body Content";

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        var emailService = new EmailService(_httpClient, _settings, _loggerMock.Object, _resiliencePolicies);

        await emailService.SendEmailAsync(toEmail, subject, body);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Failed to send email to {toEmail} after all retry attempts")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendEmailAsync_WithEmptyBody_ShouldStillSendEmail()
    {
        const string toEmail = "recipient@test.com";
        const string subject = "Test Subject";
        const string body = "";

        var responseMessage = new HttpResponseMessage(HttpStatusCode.Accepted);

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        var emailService = new EmailService(_httpClient, _settings, _loggerMock.Object, _resiliencePolicies);

       // error not thrown
       await emailService.SendEmailAsync(toEmail, subject, body);

        _httpMessageHandlerMock
            .Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendEmailAsync_WithNullBody_ShouldStillSendEmail()
    {
        const string toEmail = "recipient@test.com";
        const string subject = "Test Subject";
        const string? body = null;
        string capturedPayload = string.Empty;

        var responseMessage = new HttpResponseMessage(HttpStatusCode.Accepted);

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedPayload = await req.Content!.ReadAsStringAsync();
            })
            .ReturnsAsync(responseMessage);

        var emailService = new EmailService(_httpClient, _settings, _loggerMock.Object, _resiliencePolicies);

        // error not thrown
        await emailService.SendEmailAsync(toEmail, subject, body!);

        var payloadJson = JsonSerializer.Deserialize<JsonElement>(capturedPayload);
        var content = payloadJson.GetProperty("content")[0];

        // null body serialized empty or null json
        var bodyValue = content.GetProperty("value").GetString();
        Assert.True(bodyValue == null || bodyValue == string.Empty);
    }

    [Fact]
    public async Task SendEmailAsync_WithLargePayload_ShouldHandleCorrectly()
    {
        const string toEmail = "recipient@test.com";
        const string subject = "Test Subject with Large Content";
        var largeBody = new string('A', 10000); // 10kb
        string capturedPayload = string.Empty;

        var responseMessage = new HttpResponseMessage(HttpStatusCode.Accepted);

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedPayload = await req.Content!.ReadAsStringAsync(_);
            })
            .ReturnsAsync(responseMessage);

        var emailService = new EmailService(_httpClient, _settings, _loggerMock.Object, _resiliencePolicies);


        await emailService.SendEmailAsync(toEmail, subject, largeBody);

        // handle large payloads correctly
        var payloadJson = JsonSerializer.Deserialize<JsonElement>(capturedPayload);
        var content = payloadJson.GetProperty("content")[0];

        Assert.Equal(largeBody, content.GetProperty("value").GetString());
        Assert.True(capturedPayload.Length > 10000); // payload is large
    }
}