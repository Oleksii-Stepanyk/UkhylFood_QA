using System;
using System.Threading.Tasks;
using AutoMapper;
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
public class CourierRatingTests
{
    private readonly Mock<ICourierRepository> _courierRepoMock;
    private readonly BusinessLogic.Services.CourierService _courierService;

    public CourierRatingTests()
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
    public async Task UpdateRating_WithFirstRating_AssignsDirectly()
    {
        // arrange
        var expectedCourierId = Guid.NewGuid();
        const float targetRating = 4.5f;
        const int initialRatingCount = 0;
        const float initialRating = 0f;
        const int expectedFinalRatingCount = 1;

        var courier = new Courier 
        { 
            Id = expectedCourierId,
            Rating = initialRating,
            RatingCount = initialRatingCount
        };

        _courierRepoMock.Setup(r => r.GetByIdAsync(expectedCourierId)).ReturnsAsync(courier);
        _courierRepoMock.Setup(r => r.UpdateAsync(courier)).ReturnsAsync(courier);

        // act
        var result = await _courierService.UpdateCourierRatingAsync(expectedCourierId, targetRating);

        // assert
        Assert.True(result);
        Assert.Equal(targetRating, courier.Rating);
        Assert.Equal(expectedFinalRatingCount, courier.RatingCount);
        _courierRepoMock.Verify(r => r.UpdateAsync(courier), Times.Once);
    }

    [Fact]
    public async Task UpdateRating_WithExistingRating_CalculatesMovingAverage()
    {
        // arrange
        var expectedCourierId = Guid.NewGuid();
        
        const float initialRating = 4.0f;
        const int initialRatingCount = 1;
        
        const float incomingRating = 5.0f;
        
        const float expectedAveragedRating = 4.5f;
        const int expectedFinalRatingCount = 2;

        var courier = new Courier 
        { 
            Id = expectedCourierId,
            Rating = initialRating,
            RatingCount = initialRatingCount
        };

        _courierRepoMock.Setup(r => r.GetByIdAsync(expectedCourierId)).ReturnsAsync(courier);
        _courierRepoMock.Setup(r => r.UpdateAsync(courier)).ReturnsAsync(courier);

        // act
        var result = await _courierService.UpdateCourierRatingAsync(expectedCourierId, incomingRating);

        // assert
        Assert.True(result);
        Assert.Equal(expectedAveragedRating, courier.Rating);
        Assert.Equal(expectedFinalRatingCount, courier.RatingCount);
    }

    [Fact]
    public async Task UpdateRating_WhenCourierMissing_ThrowsInvalidOperationException()
    {
        // arrange
        var missingCourierId = Guid.NewGuid();
        const float validRating = 5.0f;
        
        _courierRepoMock.Setup(r => r.GetByIdAsync(missingCourierId)).ReturnsAsync((Courier)null!);

        // act & assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _courierService.UpdateCourierRatingAsync(missingCourierId, validRating)
        );
        
        _courierRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Courier>()), Times.Never);
    }
}
