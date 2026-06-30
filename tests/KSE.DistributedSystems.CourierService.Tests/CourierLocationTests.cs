using System;
using System.Threading.Tasks;
using AutoMapper;
using KSE.DistributedSystems.CourierService.BusinessLogic.DTOs;
using KSE.DistributedSystems.CourierService.BusinessLogic.MappingProfiles;
using KSE.DistributedSystems.CourierService.DataAccess.Entities;
using KSE.DistributedSystems.CourierService.DataAccess.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace KSE.DistributedSystems.CourierService.Tests;

[Trait("Category", "Unit")]
public class CourierLocationTests
{
    private readonly Mock<ICourierRepository> _courierRepoMock;
    private readonly BusinessLogic.Services.CourierService _courierService;

    public CourierLocationTests()
    {
        _courierRepoMock = new Mock<ICourierRepository>();
        
        var loggerMock = new Mock<ILogger<BusinessLogic.Services.CourierService>>();
        var endpointProviderMock = new Mock<ISendEndpointProvider>();
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<CourierProfile>()).CreateMapper();
        var tracer = Sdk.CreateTracerProviderBuilder().Build().GetTracer("DummyTracer");

        _courierService = new BusinessLogic.Services.CourierService(
            _courierRepoMock.Object,
            mapper,
            endpointProviderMock.Object,
            loggerMock.Object,
            tracer
        );
    }

    [Fact]
    public async Task UpdateLocation_WithExistingCourier_UpdatesLocationAndReturnsTrue()
    {
        // arrange
        var courierId = Guid.NewGuid();
        var existingCourier = new Courier 
        { 
            Id = courierId, 
            FirstName = "Courier", 
            LastName = "Six" 
        };

        _courierRepoMock.Setup(r => r.GetByIdAsync(courierId)).ReturnsAsync(existingCourier);
        _courierRepoMock.Setup(r => r.UpdateAsync(It.IsAny<Courier>())).ReturnsAsync(existingCourier);

        // act
        var result = await _courierService.UpdateCourierLocationAsync(courierId, 50.4501, 30.5234);

        // assert
        Assert.True(result);
        
        // make sure the entity got updated before being saved
        Assert.NotNull(existingCourier.CurrentLocation);
        Assert.Equal(50.4501, existingCourier.CurrentLocation.Latitude);
        Assert.Equal(30.5234, existingCourier.CurrentLocation.Longitude);
        
        _courierRepoMock.Verify(r => r.UpdateAsync(existingCourier), Times.Once);
    }

    [Fact]
    public async Task UpdateLocation_WhenCourierNotFound_ThrowsInvalidOperationException()
    {
        // arrange
        var nonExistentId = Guid.NewGuid();
        
        _courierRepoMock.Setup(r => r.GetByIdAsync(nonExistentId)).ReturnsAsync((Courier)null!);

        // act & assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _courierService.UpdateCourierLocationAsync(nonExistentId, 10, 10)
        );

        _courierRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Courier>()), Times.Never);
    }

    [Fact]
    public async Task UpdateLocation_WhenUpdateFailsInDb_ReturnsFalse()
    {
        // arrange
        var courierId = Guid.NewGuid();
        var courier = new Courier { Id = courierId };

        _courierRepoMock.Setup(r => r.GetByIdAsync(courierId)).ReturnsAsync(courier);
        // Simulate a concurrency issue or DB failure on save
        _courierRepoMock.Setup(r => r.UpdateAsync(courier)).ReturnsAsync((Courier)null!);

        // act
        var result = await _courierService.UpdateCourierLocationAsync(courierId, 12.34, 56.78);

        // assert
        Assert.False(result); // handled gracefully
    }
}
