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
public class CourierAvailabilityTests
{
    private readonly Mock<ICourierRepository> _courierRepoMock;
    private readonly BusinessLogic.Services.CourierService _courierService;

    public CourierAvailabilityTests()
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
    public async Task SetAvailability_ToTrue_UpdatesEntityProperly()
    {
        // arrange
        var courierId = Guid.NewGuid();
        var courier = new Courier { Id = courierId, IsAvailable = false }; // starting offline

        _courierRepoMock.Setup(r => r.GetByIdAsync(courierId)).ReturnsAsync(courier);
        _courierRepoMock.Setup(r => r.UpdateAsync(courier)).ReturnsAsync(courier);

        var dto = new CourierAvailabilityUpdateDto { IsAvailable = true };

        // act
        var result = await _courierService.UpdateCourierAvailabilityAsync(courierId, true);

        // assert
        Assert.True(result);
        Assert.True(courier.IsAvailable, "Courier should now be online");
        _courierRepoMock.Verify(r => r.UpdateAsync(courier), Times.Once);
    }

    [Fact]
    public async Task SetAvailability_WhenCourierMissing_ThrowsInvalidOperationException()
    {
        // arrange
        var missingId = Guid.NewGuid();
        _courierRepoMock.Setup(r => r.GetByIdAsync(missingId)).ReturnsAsync((Courier)null!);
        
        var dto = new CourierAvailabilityUpdateDto { IsAvailable = true };

        // act & assert
        // Weirdly, location update returns false but availability throws. 
        // We test that behavior here to lock it in.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _courierService.UpdateCourierAvailabilityAsync(missingId, true)
        );
        
        _courierRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Courier>()), Times.Never);
    }

    [Fact]
    public async Task SetAvailability_WithEmptyGuid_ThrowsArgumentException()
    {
        // arrange
        var dto = new CourierAvailabilityUpdateDto { IsAvailable = false };

        // act & assert
        // Guid.Empty should get caught before we even hit the repo
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _courierService.UpdateCourierAvailabilityAsync(Guid.Empty, false)
        );
        
        Assert.Contains("id cannot be empty", ex.Message.ToLower());
        _courierRepoMock.Verify(r => r.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
    }
}
