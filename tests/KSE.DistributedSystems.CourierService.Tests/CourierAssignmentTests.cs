using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using KSE.DistributedSystems.CourierService.BusinessLogic.MappingProfiles;
using KSE.DistributedSystems.CourierService.DataAccess.Entities;
using KSE.DistributedSystems.CourierService.DataAccess.Interfaces;
using KSE.DistributedSystems.OrderService.Models;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace KSE.DistributedSystems.CourierService.Tests;

[Trait("Category", "Unit")]
public class CourierAssignmentTests
{
    private readonly Mock<ICourierRepository> _courierRepoMock;
    private readonly Mock<ISendEndpointProvider> _endpointProviderMock;
    private readonly Mock<ISendEndpoint> _sendEndpointMock;
    private readonly BusinessLogic.Services.CourierService _courierService;

    public CourierAssignmentTests()
    {
        _courierRepoMock = new Mock<ICourierRepository>();
        _endpointProviderMock = new Mock<ISendEndpointProvider>();
        _sendEndpointMock = new Mock<ISendEndpoint>();
        
        var loggerMock = new Mock<ILogger<BusinessLogic.Services.CourierService>>();
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<CourierProfile>()).CreateMapper();
        var tracer = Sdk.CreateTracerProviderBuilder().Build().GetTracer("DummyTracer");

        _endpointProviderMock
            .Setup(p => p.GetSendEndpoint(It.IsAny<Uri>()))
            .ReturnsAsync(_sendEndpointMock.Object);

        _courierService = new BusinessLogic.Services.CourierService(
            _courierRepoMock.Object,
            mapper,
            _endpointProviderMock.Object,
            loggerMock.Object,
            tracer
        );
    }

    [Fact]
    public async Task AssignOrder_WithValidUnassignedOrder_AssignsToCourierAndNotifiesQueue()
    {
        // arrange
        var expectedOrderId = Guid.NewGuid();
        var expectedCourierId = Guid.NewGuid();
        
        var unassignedOrder = new Order 
        { 
            Id = expectedOrderId 
        };

        var availableCouriers = new List<Courier> 
        { 
            new Courier { Id = expectedCourierId, IsAvailable = true } 
        };

        _courierRepoMock.Setup(r => r.GetAvailableCouriersAsync()).ReturnsAsync(availableCouriers);
        _courierRepoMock.Setup(r => r.GetByIdAsync(expectedCourierId)).ReturnsAsync(availableCouriers[0]);
        _courierRepoMock.Setup(r => r.UpdateAsync(It.IsAny<Courier>())).ReturnsAsync(availableCouriers[0]);

        // act
        var resultId = await _courierService.AssignOrderToCourierAsync(unassignedOrder);

        // assert
        Assert.Equal(expectedCourierId, resultId);
        Assert.Equal(expectedCourierId, unassignedOrder.CourierId);
        
        // Verifies the courier's availability was set to false
        _courierRepoMock.Verify(r => r.UpdateAsync(It.Is<Courier>(c => c.Id == expectedCourierId && !c.IsAvailable)), Times.Once);
        
        // Verifies message sent to queue
        _sendEndpointMock.Verify(s => s.Send(unassignedOrder, default), Times.Once);
    }

    [Fact]
    public async Task AssignOrder_WithEmptyOrderId_ThrowsArgumentException()
    {
        // arrange
        var emptyOrderId = Guid.Empty;
        var order = new Order { Id = emptyOrderId };
        const string expectedExceptionMessage = "Order ID cannot be empty.";

        // act & assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _courierService.AssignOrderToCourierAsync(order)
        );
        
        Assert.Contains(expectedExceptionMessage, ex.Message);
        _courierRepoMock.Verify(r => r.GetAvailableCouriersAsync(), Times.Never);
    }

    [Fact]
    public async Task AssignOrder_AlreadyAssigned_ReturnsExistingCourierId()
    {
        // arrange
        var expectedOrderId = Guid.NewGuid();
        var existingCourierId = Guid.NewGuid();
        
        var alreadyAssignedOrder = new Order 
        { 
            Id = expectedOrderId, 
            CourierId = existingCourierId 
        };

        // act
        var resultId = await _courierService.AssignOrderToCourierAsync(alreadyAssignedOrder);

        // assert
        Assert.Equal(existingCourierId, resultId);
        _courierRepoMock.Verify(r => r.GetAvailableCouriersAsync(), Times.Never);
        _sendEndpointMock.Verify(s => s.Send(It.IsAny<Order>(), default), Times.Never);
    }

    [Fact]
    public async Task AssignOrder_NoAvailableCouriers_ThrowsInvalidOperationException()
    {
        // arrange
        var orderId = Guid.NewGuid();
        var order = new Order { Id = orderId };
        const string expectedExceptionMessage = "No available couriers found.";

        _courierRepoMock.Setup(r => r.GetAvailableCouriersAsync()).ReturnsAsync(new List<Courier>());

        // act & assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _courierService.AssignOrderToCourierAsync(order)
        );
        
        Assert.Equal(expectedExceptionMessage, ex.Message);
        _sendEndpointMock.Verify(s => s.Send(It.IsAny<Order>(), default), Times.Never);
    }
}
