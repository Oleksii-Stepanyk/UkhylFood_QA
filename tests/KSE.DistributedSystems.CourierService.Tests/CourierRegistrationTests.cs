using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
    private readonly Mock<IMapper> _mapperMock;
    private readonly BusinessLogic.Services.CourierService _courierService;

    public CourierRegistrationTests()
    {
        _courierRepoMock = new Mock<ICourierRepository>();
        _mapperMock = new Mock<IMapper>();
        
        var loggerMock = new Mock<ILogger<BusinessLogic.Services.CourierService>>();
        var endpointProviderMock = new Mock<ISendEndpointProvider>();
        
        var tracer = Sdk.CreateTracerProviderBuilder().Build().GetTracer("DummyTracer");

        _courierService = new BusinessLogic.Services.CourierService(
            _courierRepoMock.Object,
            _mapperMock.Object,
            endpointProviderMock.Object,
            loggerMock.Object,
            tracer
        );
    }

    [Fact]
    public async Task RegisterCourier_WithValidData_ReturnsCorrectReadDto()
    {
        // arrange
        const string expectedFirstName = "Courier";
        const string expectedLastName = "Six";
        const string expectedVehicleType = "Bike";
        var expectedFullName = $"{expectedFirstName} {expectedLastName}";

        var dto = new CourierRegistrationDto
        {
            FirstName = expectedFirstName,
            LastName = expectedLastName,
            VehicleType = expectedVehicleType
        };

        var courierEntity = new Courier 
        { 
            Id = Guid.NewGuid(), 
            FirstName = expectedFirstName, 
            LastName = expectedLastName, 
            VehicleType = Enum.Parse<VehicleType>(expectedVehicleType) 
        };
        
        var readDto = new CourierReadDto
        {
            Id = courierEntity.Id,
            FullName = expectedFullName,
            VehicleType = expectedVehicleType,
            CurrentLocation = new GeoPoint(0, 0)
        };

        _mapperMock.Setup(m => m.Map<Courier>(dto)).Returns(courierEntity);
        _mapperMock.Setup(m => m.Map<CourierReadDto>(courierEntity)).Returns(readDto);

        _courierRepoMock
            .Setup(repo => repo.AddAsync(It.IsAny<Courier>()))
            .ReturnsAsync((Courier c) => c);

        // act
        var result = await _courierService.RegisterCourierAsync(dto);

        // assert
        Assert.NotNull(result);
        Assert.Equal(expectedFullName, result.FullName);
        Assert.Equal(expectedVehicleType, result.VehicleType);
        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task RegisterCourier_WhenRepositoryFails_ThrowsInvalidOperationException()
    {
        // arrange
        const string expectedFirstName = "Courier";
        const string expectedLastName = "Seven";
        const string expectedVehicleType = "Scooter";
        const string expectedExceptionMessage = "Failed to register courier";

        var dto = new CourierRegistrationDto
        {
            FirstName = expectedFirstName,
            LastName = expectedLastName,
            VehicleType = expectedVehicleType
        };

        var courierEntity = new Courier { Id = Guid.NewGuid() };
        _mapperMock.Setup(m => m.Map<Courier>(dto)).Returns(courierEntity);

        _courierRepoMock
            .Setup(repo => repo.AddAsync(It.IsAny<Courier>()))
            .ReturnsAsync((Courier)null!);

        // act & assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _courierService.RegisterCourierAsync(dto)
        );
        
        Assert.Contains(expectedExceptionMessage, ex.Message);
    }

    [Fact]
    public async Task RegisterCourier_PassesNullDto_ThrowsArgumentNullException()
    {
        // arrange
        CourierRegistrationDto nullDto = null!;

        // act & assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _courierService.RegisterCourierAsync(nullDto)
        );
        
        _courierRepoMock.Verify(repo => repo.AddAsync(It.IsAny<Courier>()), Times.Never);
    }
    [Fact]
    public async Task RegisterCourier_WhenMapperFails_ThrowsInvalidOperationException()
    {
        // arrange
        const string expectedFirstName = "Courier";
        const string expectedLastName = "Six";
        const string expectedVehicleType = "Bike";
        const string expectedExceptionMessage = "Failed to create courier from registration data";

        var dto = new CourierRegistrationDto
        {
            FirstName = expectedFirstName,
            LastName = expectedLastName,
            VehicleType = expectedVehicleType
        };

        _mapperMock.Setup(m => m.Map<Courier>(dto)).Returns((Courier)null!);

        // act & assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _courierService.RegisterCourierAsync(dto)
        );
        
        Assert.Contains(expectedExceptionMessage, ex.Message);
        _courierRepoMock.Verify(repo => repo.AddAsync(It.IsAny<Courier>()), Times.Never);
    }

    [Theory]
    [InlineData("A", "Six", "Bike", "First name must be at least 2 characters")]
    [InlineData("Courier", "A", "Bike", "Last name must be at least 3 characters")]
    [InlineData("Courier", "Six", "Spaceship", "Invalid value")]
    public void RegisterCourierDto_InvalidAttributes_FailsValidation(
        string firstName, string lastName, string vehicleType, string expectedErrorSubstring)
    {
        // arrange
        var dto = new CourierRegistrationDto
        {
            FirstName = firstName,
            LastName = lastName,
            VehicleType = vehicleType
        };
        var context = new ValidationContext(dto);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        // act
        var isValid = Validator.TryValidateObject(dto, context, results, validateAllProperties: true);

        // assert
        Assert.False(isValid);
        Assert.Contains(results, r => r.ErrorMessage!.Contains(expectedErrorSubstring));
    }
}
