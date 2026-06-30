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
        var expectedCourierId = Guid.NewGuid();
        const string expectedFirstName = "Courier";
        const string expectedLastName = "Six";
        const double expectedLatitude = 50.4501;
        const double expectedLongitude = 30.5234;

        var existingCourier = new Courier 
        { 
            Id = expectedCourierId, 
            FirstName = expectedFirstName, 
            LastName = expectedLastName 
        };

        _courierRepoMock.Setup(r => r.GetByIdAsync(expectedCourierId)).ReturnsAsync(existingCourier);
        _courierRepoMock.Setup(r => r.UpdateAsync(It.IsAny<Courier>())).ReturnsAsync(existingCourier);

        // act
        var result = await _courierService.UpdateCourierLocationAsync(expectedCourierId, expectedLatitude, expectedLongitude);

        // assert
        Assert.True(result);
        
        Assert.NotNull(existingCourier.CurrentLocation);
        Assert.Equal(expectedLatitude, existingCourier.CurrentLocation.Latitude);
        Assert.Equal(expectedLongitude, existingCourier.CurrentLocation.Longitude);
        
        _courierRepoMock.Verify(r => r.UpdateAsync(existingCourier), Times.Once);
    }

    [Fact]
    public async Task UpdateLocation_WhenCourierNotFound_ThrowsInvalidOperationException()
    {
        // arrange
        var nonExistentId = Guid.NewGuid();
        const double testLatitude = 10.0;
        const double testLongitude = 10.0;
        
        _courierRepoMock.Setup(r => r.GetByIdAsync(nonExistentId)).ReturnsAsync((Courier)null!);

        // act & assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _courierService.UpdateCourierLocationAsync(nonExistentId, testLatitude, testLongitude)
        );

        _courierRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Courier>()), Times.Never);
    }

    [Fact]
    public async Task UpdateLocation_WhenUpdateFailsInDb_ReturnsFalse()
    {
        // arrange
        var expectedCourierId = Guid.NewGuid();
        const double testLatitude = 12.34;
        const double testLongitude = 56.78;

        var courier = new Courier { Id = expectedCourierId };

        _courierRepoMock.Setup(r => r.GetByIdAsync(expectedCourierId)).ReturnsAsync(courier);
        // Simulate a concurrency issue or DB failure on save
        _courierRepoMock.Setup(r => r.UpdateAsync(courier)).ReturnsAsync((Courier)null!);

        // act
        var result = await _courierService.UpdateCourierLocationAsync(expectedCourierId, testLatitude, testLongitude);

        // assert
        Assert.False(result); // handled gracefully
    }
}
