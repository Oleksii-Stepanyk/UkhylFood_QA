using System;
using System.Threading.Tasks;
using KSE.DistributedSystems.NotificationService.Consumers;
using KSE.DistributedSystems.NotificationService.Models;
using KSE.DistributedSystems.NotificationService.Services;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace KSE.DistributedSystems.NotificationService.Tests.ContractTests;

public class OrderCreatedConsumerContractTests : IAsyncLifetime
{
    private ServiceProvider _provider;
    private ITestHarness _harness;
    private Mock<IEmailService> _emailServiceMock;

    public async Task InitializeAsync()
    {
        _emailServiceMock = new Mock<IEmailService>();
        _emailServiceMock.Setup(s => s.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(_emailServiceMock.Object);
        services.AddSingleton(Mock.Of<ILogger<OrderCreatedConsumer>>());

        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<OrderCreatedConsumer>();
        });

        _provider = services.BuildServiceProvider(true);
        _harness = _provider.GetRequiredService<ITestHarness>();
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task OrderCreatedConsumer_Should_Consume_OrderCreated_Message_And_Send_Email()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var email = "test@example.com";
        var expectedMessage = new OrderCreated
        {
            OrderId = orderId,
            Email = email
        };

        // Act
        await _harness.Bus.Publish(expectedMessage);

        // Assert
        Assert.True(await _harness.Consumed.Any<OrderCreated>());
        
        var consumerHarness = _harness.GetConsumerHarness<OrderCreatedConsumer>();
        Assert.True(await consumerHarness.Consumed.Any<OrderCreated>());

        _emailServiceMock.Verify(s => s.SendEmailAsync(
            email, 
            It.Is<string>(subject => subject.Contains("Order Created")), 
            It.Is<string>(body => body.Contains(orderId.ToString()))), 
            Times.Once);
    }
}
