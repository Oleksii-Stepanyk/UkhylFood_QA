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
public class CourierRetrievalTests
{
    private readonly Mock<ICourierRepository> _courierRepoMock;
    private readonly BusinessLogic.Services.CourierService _courierService;

    public CourierRetrievalTests()
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
    public async Task GetCourierById_WithValidId_ReturnsMappedReadDto()
    {
        // arrange
        var expectedCourierId = Guid.NewGuid();
        const string expectedFirstName = "Test";
        const string expectedLastName = "Courier";
        var expectedFullName = $"{expectedFirstName} {expectedLastName}";

        var courier = new Courier 
        { 
            Id = expectedCourierId,
            FirstName = expectedFirstName,
            LastName = expectedLastName
        };

        _courierRepoMock.Setup(r => r.GetByIdAsync(expectedCourierId)).ReturnsAsync(courier);

        // act
        var result = await _courierService.GetCourierByIdAsync(expectedCourierId);

        // assert
        Assert.NotNull(result);
        Assert.Equal(expectedCourierId, result!.Id);
        Assert.Equal(expectedFullName, result.FullName);
    }

    [Fact]
    public async Task GetCourierById_WhenMissing_ReturnsNull()
    {
        // arrange
        var missingCourierId = Guid.NewGuid();
        
        _courierRepoMock.Setup(r => r.GetByIdAsync(missingCourierId)).ReturnsAsync((Courier)null!);

        // act
        var result = await _courierService.GetCourierByIdAsync(missingCourierId);

        // assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetCourierById_WithEmptyGuid_ThrowsArgumentException()
    {
        // arrange
        var emptyId = Guid.Empty;
        const string expectedExceptionMessageFragment = "id cannot be empty";

        // act & assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _courierService.GetCourierByIdAsync(emptyId)
        );
        
        Assert.Contains(expectedExceptionMessageFragment, ex.Message.ToLower());
    }

    [Fact]
    public async Task GetCourierLocation_WithValidId_ReturnsGeoPoint()
    {
        // arrange
        var expectedCourierId = Guid.NewGuid();
        const double expectedLatitude = 50.4501;
        const double expectedLongitude = 30.5234;
        
        var expectedLocation = new GeoPoint(expectedLatitude, expectedLongitude);

        _courierRepoMock.Setup(r => r.GetCourierLocationAsync(expectedCourierId)).ReturnsAsync(expectedLocation);

        // act
        var result = await _courierService.GetCourierLocationAsync(expectedCourierId);

        // assert
        Assert.NotNull(result);
        Assert.Equal(expectedLatitude, result!.Latitude);
        Assert.Equal(expectedLongitude, result!.Longitude);
    }

    [Fact]
    public async Task GetCourierLocation_WhenMissing_ReturnsNull()
    {
        // arrange
        var missingCourierId = Guid.NewGuid();
        
        _courierRepoMock.Setup(r => r.GetCourierLocationAsync(missingCourierId)).ReturnsAsync((GeoPoint)null!);

        // act
        var result = await _courierService.GetCourierLocationAsync(missingCourierId);

        // assert
        Assert.Null(result);
    }
}
