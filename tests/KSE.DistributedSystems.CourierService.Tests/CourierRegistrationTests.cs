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
public class CourierRegistrationTests
{
    private readonly Mock<ICourierRepository> _courierRepoMock;
    private readonly BusinessLogic.Services.CourierService _courierService;

    public CourierRegistrationTests()
    {
        // Set up the basic dependencies we need for the service
        _courierRepoMock = new Mock<ICourierRepository>();
        
        var loggerMock = new Mock<ILogger<BusinessLogic.Services.CourierService>>();
        var endpointProviderMock = new Mock<ISendEndpointProvider>();
        
        // Grab the mapper from our actual profiles so we test real mapping rules
        var mapperConfig = new MapperConfiguration(cfg => cfg.AddProfile<CourierProfile>());
        var mapper = mapperConfig.CreateMapper();
        
        // Just use a dummy tracer for the telemetry stuff
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
    public async Task RegisterCourier_WithValidData_ReturnsCorrectReadDto()
    {
        // arrange
        var dto = new CourierRegistrationDto
        {
            FirstName = "Courier",
            LastName = "Six",
            VehicleType = "Bike"
        };

        // We expect the repo to return the same courier we pass it
        _courierRepoMock
            .Setup(repo => repo.AddAsync(It.IsAny<Courier>()))
            .ReturnsAsync((Courier c) => c);

        // act
        var result = await _courierService.RegisterCourierAsync(dto);

        // assert
        Assert.NotNull(result);
        Assert.Equal("Courier Six", result.FullName);
        Assert.Equal("Bike", result.VehicleType);
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task RegisterCourier_WhenRepositoryFails_ThrowsInvalidOperationException()
    {
        // arrange
        var dto = new CourierRegistrationDto
        {
            FirstName = "Courier",
            LastName = "Seven",
            VehicleType = "Scooter"
        };

        // repo returns null simulating a db issue or constraint fail
        _courierRepoMock
            .Setup(repo => repo.AddAsync(It.IsAny<Courier>()))
            .ReturnsAsync((Courier)null!);

        // act & assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _courierService.RegisterCourierAsync(dto)
        );
        
        Assert.Contains("Failed to register courier", ex.Message);
    }

    [Fact]
    public async Task RegisterCourier_PassesNullDto_ThrowsArgumentNullException()
    {
        // act & assert
        // Should explode if someone tries to pass a null dto directly to the service
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _courierService.RegisterCourierAsync(null!)
        );
        
        // Double check we never even hit the db
        _courierRepoMock.Verify(repo => repo.AddAsync(It.IsAny<Courier>()), Times.Never);
    }
}
