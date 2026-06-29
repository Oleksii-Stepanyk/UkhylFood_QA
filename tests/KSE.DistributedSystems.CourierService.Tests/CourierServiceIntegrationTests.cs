using AutoMapper;
using KSE.DistributedSystems.CourierService.BusinessLogic.DTOs;
using KSE.DistributedSystems.CourierService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.CourierService.BusinessLogic.MappingProfiles;
using KSE.DistributedSystems.CourierService.BusinessLogic.Services;
using KSE.DistributedSystems.CourierService.DataAccess;
using KSE.DistributedSystems.CourierService.DataAccess.Entities;
using KSE.DistributedSystems.CourierService.DataAccess.Interfaces;
using KSE.DistributedSystems.CourierService.DataAccess.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using System.ComponentModel;
using Testcontainers.Redis;
using Xunit;
using Order = KSE.DistributedSystems.OrderService.Models.Order;

namespace KSE.DistributedSystems.CourierService.Tests;

[Trait("Category", "Integration")]
public class CourierServiceIntegrationTests
{
    private readonly ICourierService _courierService;
    private readonly ICourierRepository _courierRepository;
    private readonly CourierDbContext _dbContext;

    public CourierServiceIntegrationTests()
    {
        var redisContainer = new RedisBuilder().Build();
        redisContainer.StartAsync().GetAwaiter().GetResult();
        var connection = ConnectionMultiplexer.Connect(redisContainer.GetConnectionString());

        var mockEndpoint = new Mock<ISendEndpoint>();
        mockEndpoint.Setup(e => e.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockProvider = new Mock<ISendEndpointProvider>();
        mockProvider.Setup(p => p.GetSendEndpoint(It.IsAny<Uri>()))
            .ReturnsAsync(mockEndpoint.Object);

        var mapper = new MapperConfiguration(cfg => { cfg.AddProfile(new CourierProfile()); })
            .CreateMapper();

        var tracer = new TracerProviderBuilderBase().Build().GetTracer("CourierService.Tests");
        var options = new DbContextOptionsBuilder<CourierDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new CourierDbContext(options);
        _courierRepository = new CourierRepository(_dbContext, new Mock<ILogger<CourierRepository>>().Object, tracer);
        var courierService = new BusinessLogic.Services.CourierService(_courierRepository, mapper, mockProvider.Object,
            new Mock<ILogger<BusinessLogic.Services.CourierService>>().Object, tracer);
        _courierService = new CourierServiceWithCache(courierService, connection,
            new Mock<ILogger<CourierServiceWithCache>>().Object, tracer);
    }

    [Fact]
    public async Task RegisterCourierAndRetrieveById_ShouldReturnCorrectCourier()
    {
        var registrationDto = new CourierRegistrationDto
        {
            FirstName = "Courier",
            LastName = "Six",
            VehicleType = "Foot"
        };

        var registered = await _courierService.RegisterCourierAsync(registrationDto);

        Assert.NotNull(registered);
        Assert.Equal("Courier Six", registered.FullName);
        Assert.True(registered.Id != Guid.Empty);

        var fetched = await _courierService.GetCourierByIdAsync(registered.Id);

        Assert.NotNull(fetched);
        Assert.Equal(registered.Id, fetched!.Id);
        Assert.Equal("Courier Six", fetched.FullName);
    }

    [Fact]
    public async Task UpdateAndFetchCourierLocation_ShouldUseRedisCache()
    {
        var courier = new Courier
        {
            Id = Guid.NewGuid(),
            FirstName = "Courier",
            LastName = "Six",
            IsAvailable = true,
            VehicleType = VehicleType.Foot,
            CurrentLocation = new GeoPoint(32, 23)
        };

        _dbContext.Couriers.Add(courier);
        await _dbContext.SaveChangesAsync();

        var updated = await _courierService.UpdateCourierLocationAsync(courier.Id, 40.0, -74.0);

        Assert.True(updated);

        var location = await _courierService.GetCourierLocationAsync(courier.Id);

        Assert.NotNull(location);
        Assert.Equal(40.0, location!.Latitude);
        Assert.Equal(-74.0, location.Longitude);
    }

    [Fact]
    public async Task AssignOrderToCourier_ShouldAssignAndMarkCourierUnavailable()
    {
        var courier = new Courier
        {
            Id = Guid.NewGuid(),
            FirstName = "Courier",
            LastName = "Six",
            IsAvailable = true,
            VehicleType = VehicleType.Foot,
            CurrentLocation = new GeoPoint(32, 23)
        };

        await _courierRepository.AddAsync(courier);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CourierId = null
        };

        var assignedCourierId = await _courierService.AssignOrderToCourierAsync(order);

        Assert.Equal(courier.Id, assignedCourierId);

        var updatedCourier = await _courierService.GetCourierByIdAsync(courier.Id);

        Assert.NotNull(updatedCourier);
        Assert.False(updatedCourier.IsAvailable);
    }
}