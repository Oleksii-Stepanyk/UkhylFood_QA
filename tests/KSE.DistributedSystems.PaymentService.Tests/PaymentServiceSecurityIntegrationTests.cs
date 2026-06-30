using System;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;

namespace KSE.DistributedSystems.PaymentService.Tests;

public class PaymentServiceSecurityIntegrationTests
{
    private WebApplicationFactory<KSE.DistributedSystems.PaymentService.Program> _factory;
    private HttpClient _client;

    private const string JwtSecret = "super-secret-key-for-testing-purposes-12345";
    private const string Issuer = "UkhylFood";
    private const string Audience = "UkhylFoodClients";

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new WebApplicationFactory<KSE.DistributedSystems.PaymentService.Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, configBuilder) =>
            {
                // Override settings to avoid crashes on startup (like missing RabbitMQ)
                var config = new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string>("RabbitMQ:Host", "localhost"),
                    new System.Collections.Generic.KeyValuePair<string, string>("ConnectionStrings:DefaultConnection", ""), // Use in-memory DB
                    new System.Collections.Generic.KeyValuePair<string, string>("Jwt:Key", JwtSecret),
                    new System.Collections.Generic.KeyValuePair<string, string>("Jwt:Issuer", Issuer),
                    new System.Collections.Generic.KeyValuePair<string, string>("Jwt:Audience", Audience)
                };
                configBuilder.AddInMemoryCollection(config);
            });
            
        });

        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private string GenerateJwtToken(string key, int expirationMinutes = 60)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "User")
        };

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [Test]
    public async Task Unauthenticated_Request_WithoutToken_Returns401()
    {
        // Act
        // Send a POST request to the payment processing endpoint without setting the Authorization header
        var response = await _client.PostAsync("/api/v1/payments", new StringContent("{}", Encoding.UTF8, "application/json"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Unauthenticated_Request_WithInvalidTokenSignature_Returns401()
    {
        // Arrange
        // Sign with a completely different secret key
        var invalidToken = GenerateJwtToken("some-fake-secret-key-that-is-very-long-12345");
        
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", invalidToken);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

}
