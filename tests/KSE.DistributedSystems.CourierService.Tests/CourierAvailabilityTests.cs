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
        var expectedCourierId = Guid.NewGuid();
        const bool initialAvailability = false;
        const bool targetAvailability = true;

        var courier = new Courier { Id = expectedCourierId, IsAvailable = initialAvailability };

        _courierRepoMock.Setup(r => r.GetByIdAsync(expectedCourierId)).ReturnsAsync(courier);
        _courierRepoMock.Setup(r => r.UpdateAsync(courier)).ReturnsAsync(courier);

        // act
        var result = await _courierService.UpdateCourierAvailabilityAsync(expectedCourierId, targetAvailability);

        // assert
        Assert.True(result);
        Assert.Equal(targetAvailability, courier.IsAvailable);
        _courierRepoMock.Verify(r => r.UpdateAsync(courier), Times.Once);
    }

    [Fact]
    public async Task SetAvailability_WhenCourierMissing_ThrowsInvalidOperationException()
    {
        // arrange
        var missingId = Guid.NewGuid();
        const bool targetAvailability = true;

        _courierRepoMock.Setup(r => r.GetByIdAsync(missingId)).ReturnsAsync((Courier)null!);

        // act & assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _courierService.UpdateCourierAvailabilityAsync(missingId, targetAvailability)
        );
        
        _courierRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Courier>()), Times.Never);
    }

    [Fact]
    public async Task SetAvailability_WithEmptyGuid_ThrowsArgumentException()
    {
        // arrange
        var emptyId = Guid.Empty;
        const bool targetAvailability = false;
        const string expectedExceptionMessageFragment = "id cannot be empty";

        // act & assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _courierService.UpdateCourierAvailabilityAsync(emptyId, targetAvailability)
        );
        
        Assert.Contains(expectedExceptionMessageFragment, ex.Message.ToLower());
        _courierRepoMock.Verify(r => r.GetByIdAsync(It.IsAny<Guid>()), Times.Never);
    }
}
