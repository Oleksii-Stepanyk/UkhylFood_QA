using System.ComponentModel;
using System.Text.Json;
using AutoMapper;
using KSE.DistributedSystems.CourierService.BusinessLogic.DTOs;
using KSE.DistributedSystems.CourierService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.CourierService.BusinessLogic.MappingProfiles;
using KSE.DistributedSystems.CourierService.BusinessLogic.Services;
using KSE.DistributedSystems.CourierService.DataAccess.Entities;
using KSE.DistributedSystems.CourierService.DataAccess.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using OpenTelemetry;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using Xunit;
using Order = KSE.DistributedSystems.OrderService.Models.Order;

namespace KSE.DistributedSystems.CourierService.Tests;

[Trait("Category", "Unit")]
public class CourierServiceUnitTests
{
    private readonly Mock<ICourierRepository> _courierRepository;
    private readonly Mock<IDatabase> _db;
    private readonly ICourierService _courierService;

    public CourierServiceUnitTests()
    {
        _courierRepository = new Mock<ICourierRepository>();
        _db = new Mock<IDatabase>();

        var mockEndpointProvider = new Mock<ISendEndpointProvider>();
        var mockMultiplexer = new Mock<IConnectionMultiplexer>();
        var mockLogger = new Mock<ILogger<BusinessLogic.Services.CourierService>>();
        var tracer = Sdk.CreateTracerProviderBuilder().Build().GetTracer("TestTracer");
        var mapper = new MapperConfiguration(cfg => { cfg.AddProfile(new CourierProfile()); })
            .CreateMapper();

        mockMultiplexer.Setup(c => c.IsConnected).Returns(true);
        mockMultiplexer
            .Setup(c => c.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_db.Object);

        _db.Setup(db => db.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<When>(),
            It.IsAny<CommandFlags>())).ReturnsAsync(true);

        var courierService = new BusinessLogic.Services.CourierService(_courierRepository.Object, mapper,
            mockEndpointProvider.Object, mockLogger.Object, tracer);
        _courierService = new CourierServiceWithCache(courierService, mockMultiplexer.Object,
            new Mock<ILogger<CourierServiceWithCache>>().Object, tracer);
    }

    [Fact]
    public async Task GetCourierByIdAsync_ValidId_ReturnsCourierReadDto()
    {
        var courierId = Guid.NewGuid();
        var courier = new Courier
        {
            Id = courierId,
            FirstName = "Test",
            LastName = "Courier",
            IsAvailable = true,
            Rating = 4.5f,
            VehicleType = VehicleType.Foot,
            CurrentLocation = new GeoPoint(33, 33)
        };

        _courierRepository.Setup(r => r.GetByIdAsync(courierId)).ReturnsAsync(courier);

        var result = await _courierService.GetCourierByIdAsync(courierId);

        Assert.NotNull(result);
        Assert.Equal(courierId, result!.Id);
        Assert.Equal("Test Courier", result.FullName);
    }

    [Fact]
    public async Task GetCourierByIdAsync_EmptyId_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _courierService.GetCourierByIdAsync(Guid.Empty));
        Assert.Equal("Courier ID cannot be empty. (Parameter 'courierId')", ex.Message);
    }

    [Fact]
    public async Task RegisterCourierAsync_NullDto_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _courierService.RegisterCourierAsync(null!));
    }

    [Fact]
    public async Task RegisterCourierAsync_ValidDto_ReturnsCourierReadDto()
    {
        var dto = new CourierRegistrationDto
        {
            FirstName = "Courier",
            LastName = "Six",
            VehicleType = "Foot"
        };

        var entity = new Courier
        {
            Id = Guid.NewGuid(),
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            VehicleType = Enum.Parse<VehicleType>(dto.VehicleType, true),
            IsAvailable = true,
            Rating = 0,
            CurrentLocation = new GeoPoint(0, 0)
        };

        _courierRepository.Setup(r => r.AddAsync(It.IsAny<Courier>())).ReturnsAsync(entity);

        var result = await _courierService.RegisterCourierAsync(dto);

        Assert.Equal(entity.Id, result!.Id);
    }

    [Fact]
    public async Task AssignOrderToCourierAsync_NoCouriers_ThrowsInvalidOperationException()
    {
        var order = new Order { Id = Guid.NewGuid() };
        _courierRepository.Setup(r => r.GetAvailableCouriersAsync()).ReturnsAsync(new List<Courier>());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _courierService.AssignOrderToCourierAsync(order));
    }

    [Fact]
    public async Task UpdateCourierAvailabilityAsync_ValidCourier_ReturnsTrue()
    {
        var courier = new Courier { Id = Guid.NewGuid() };
        _courierRepository.Setup(r => r.GetByIdAsync(courier.Id)).ReturnsAsync(courier);
        _courierRepository.Setup(r => r.UpdateAsync(courier)).ReturnsAsync(courier);

        var result = await _courierService.UpdateCourierAvailabilityAsync(courier.Id, false);

        Assert.True(result);
    }

    [Fact]
    public async Task UpdateCourierAvailabilityAsync_CourierNotFound_ThrowsInvalidOperationException()
    {
        var id = Guid.NewGuid();
        _courierRepository.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Courier?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _courierService.UpdateCourierAvailabilityAsync(id, true));
    }

    [Fact]
    public async Task GetCourierLocationAsync_CacheHit_ReturnsLocation()
    {
        var id = Guid.NewGuid();
        var geoPoint = new GeoPoint { Latitude = 1.0, Longitude = 2.0 };
        var json = JsonSerializer.Serialize(geoPoint);

        _db.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>())).ReturnsAsync(json);

        var result = await _courierService.GetCourierLocationAsync(id);

        Assert.NotNull(result);
        Assert.Equal(geoPoint.Latitude, result!.Latitude);
        Assert.Equal(geoPoint.Longitude, result.Longitude);
    }

    [Fact]
    public async Task UpdateCourierRatingAsync_ValidCourier_UpdatesRating()
    {
        var id = Guid.NewGuid();
        var courier = new Courier { Id = id, Rating = 4.0f, RatingCount = 1 };
        _courierRepository.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(courier);
        _courierRepository.Setup(r => r.UpdateAsync(courier)).ReturnsAsync(courier);

        var result = await _courierService.UpdateCourierRatingAsync(id, 5.0f);

        Assert.True(result);
        Assert.Equal(2, courier.RatingCount);
    }

    [Fact]
    public async Task UpdateCourierLocationAsync_ValidCourier_UpdatesAndCachesLocation()
    {
        var id = Guid.NewGuid();
        var location = new GeoPoint();
        var courier = new Courier { Id = id, CurrentLocation = location };

        _courierRepository.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(courier);
        _courierRepository.Setup(r => r.UpdateAsync(courier)).ReturnsAsync(courier);

        var result = await _courierService.UpdateCourierLocationAsync(id, 50.0, 60.0);

        Assert.True(result);
    }
}