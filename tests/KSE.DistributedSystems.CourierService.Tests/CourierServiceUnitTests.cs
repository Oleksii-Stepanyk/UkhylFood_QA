using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using DataAnnotationsValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using KSE.DistributedSystems.CourierService.BusinessLogic.Attributes;
using KSE.DistributedSystems.CourierService.BusinessLogic.DTOs;
using KSE.DistributedSystems.CourierService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.CourierService.BusinessLogic.MappingProfiles;
using KSE.DistributedSystems.CourierService.BusinessLogic.Services;
using KSE.DistributedSystems.CourierService.BusinessLogic.Consumers;
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
using OrderStatus = KSE.DistributedSystems.OrderService.Models.OrderStatus;

namespace KSE.DistributedSystems.CourierService.Tests;

// ===========================================================================
//  PART 1 — CourierService (core, no cache) unit tests
// ===========================================================================

/// <summary>
/// White-box unit tests for CourierService.
/// All external dependencies are mocked; real AutoMapper + real OpenTelemetry
/// no-op tracer are used so span-manipulation code paths are executed.
/// </summary>
[Trait("Category", "Unit")]
public class CourierServiceCoreTests
{
    private readonly Mock<ICourierRepository>         _repo;
    private readonly Mock<ISendEndpointProvider>      _endpointProvider;
    private readonly Mock<ILogger<BusinessLogic.Services.CourierService>> _logger;
    private readonly IMapper                          _mapper;
    private readonly Tracer                           _tracer;
    private readonly BusinessLogic.Services.CourierService _sut;

    public CourierServiceCoreTests()
    {
        _repo             = new Mock<ICourierRepository>();
        _endpointProvider = new Mock<ISendEndpointProvider>();
        _logger           = new Mock<ILogger<BusinessLogic.Services.CourierService>>();
        _tracer           = Sdk.CreateTracerProviderBuilder().Build().GetTracer("UnitTest");
        _mapper           = new MapperConfiguration(cfg => cfg.AddProfile<CourierProfile>()).CreateMapper();

        _sut = new BusinessLogic.Services.CourierService(
            _repo.Object,
            _mapper,
            _endpointProvider.Object,
            _logger.Object,
            _tracer);
    }

    // -----------------------------------------------------------------------
    //  Constructor guard-clause tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_NullRepository_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new BusinessLogic.Services.CourierService(null!, _mapper, _endpointProvider.Object, _logger.Object, _tracer));
        Assert.Equal("repository", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullMapper_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new BusinessLogic.Services.CourierService(_repo.Object, null!, _endpointProvider.Object, _logger.Object, _tracer));
        Assert.Equal("mapper", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullSendEndpointProvider_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new BusinessLogic.Services.CourierService(_repo.Object, _mapper, null!, _logger.Object, _tracer));
        Assert.Equal("sendEndpointProvider", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new BusinessLogic.Services.CourierService(_repo.Object, _mapper, _endpointProvider.Object, null!, _tracer));
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullTracer_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new BusinessLogic.Services.CourierService(_repo.Object, _mapper, _endpointProvider.Object, _logger.Object, null!));
    }

    // -----------------------------------------------------------------------
    //  GetCourierByIdAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetCourierByIdAsync_EmptyGuid_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.GetCourierByIdAsync(Guid.Empty));

        Assert.Equal("courierId", ex.ParamName);
        Assert.Contains("Courier ID cannot be empty", ex.Message);
    }

    [Fact]
    public async Task GetCourierByIdAsync_ExistingCourier_ReturnsMappedDto()
    {
        var id = Guid.NewGuid();
        var entity = BuildCourier(id);
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(entity);

        var result = await _sut.GetCourierByIdAsync(id);

        Assert.NotNull(result);
        Assert.Equal(id,          result!.Id);
        Assert.Equal("Alpha Beta", result.FullName);
        Assert.Equal("Bike",       result.VehicleType);
        Assert.True(result.IsAvailable);
        Assert.Equal(Math.Round(4.5f, 2), result.Rating, precision: 5);
    }

    [Fact]
    public async Task GetCourierByIdAsync_NonExistentCourier_ReturnsNull()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Courier?)null);

        var result = await _sut.GetCourierByIdAsync(id);

        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    //  GetCourierLocationAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetCourierLocationAsync_EmptyGuid_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.GetCourierLocationAsync(Guid.Empty));
        Assert.Equal("courierId", ex.ParamName);
    }

    [Fact]
    public async Task GetCourierLocationAsync_ExistingLocation_ReturnsGeoPoint()
    {
        var id       = Guid.NewGuid();
        var expected = new GeoPoint(48.45, 34.98);
        _repo.Setup(r => r.GetCourierLocationAsync(id)).ReturnsAsync(expected);

        var result = await _sut.GetCourierLocationAsync(id);

        Assert.NotNull(result);
        Assert.Equal(expected.Latitude,  result!.Latitude);
        Assert.Equal(expected.Longitude, result.Longitude);
    }

    [Fact]
    public async Task GetCourierLocationAsync_NoLocation_ReturnsNull()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetCourierLocationAsync(id)).ReturnsAsync((GeoPoint?)null);

        var result = await _sut.GetCourierLocationAsync(id);

        Assert.Null(result);
    }

    // -----------------------------------------------------------------------
    //  RegisterCourierAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RegisterCourierAsync_NullDto_ThrowsArgumentNullException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => _sut.RegisterCourierAsync(null!));
        Assert.Equal("registrationDto", ex.ParamName);
    }

    [Fact]
    public async Task RegisterCourierAsync_RepositoryReturnsNull_ThrowsInvalidOperationException()
    {
        var dto = BuildRegistrationDto();
        _repo.Setup(r => r.AddAsync(It.IsAny<Courier>())).ReturnsAsync((Courier?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RegisterCourierAsync(dto));
        Assert.Contains("Failed to register courier", ex.Message);
    }

    [Fact]
    public async Task RegisterCourierAsync_ValidDto_ReturnsMappedCourierReadDto()
    {
        var dto = BuildRegistrationDto("Ivan", "Petrenko", "Scooter");
        var entity = new Courier
        {
            Id              = Guid.NewGuid(),
            FirstName       = dto.FirstName,
            LastName        = dto.LastName,
            VehicleType     = VehicleType.Scooter,
            IsAvailable     = true,
            CurrentLocation = new GeoPoint(0, 0)
        };
        _repo.Setup(r => r.AddAsync(It.IsAny<Courier>())).ReturnsAsync(entity);

        var result = await _sut.RegisterCourierAsync(dto);

        Assert.NotNull(result);
        Assert.Equal(entity.Id,       result!.Id);
        Assert.Equal("Ivan Petrenko", result.FullName);
        Assert.Equal("Scooter",       result.VehicleType);
    }

    [Theory]
    [InlineData("Foot")]
    [InlineData("Bike")]
    [InlineData("Scooter")]
    [InlineData("Car")]
    public async Task RegisterCourierAsync_AllVehicleTypes_MapsCorrectly(string vehicleType)
    {
        var dto = BuildRegistrationDto(vehicleType: vehicleType);
        var entity = new Courier
        {
            Id              = Guid.NewGuid(),
            FirstName       = dto.FirstName,
            LastName        = dto.LastName,
            VehicleType     = Enum.Parse<VehicleType>(vehicleType, ignoreCase: true),
            CurrentLocation = new GeoPoint()
        };
        _repo.Setup(r => r.AddAsync(It.IsAny<Courier>())).ReturnsAsync(entity);

        var result = await _sut.RegisterCourierAsync(dto);

        Assert.Equal(vehicleType, result!.VehicleType);
    }

    // -----------------------------------------------------------------------
    //  UpdateCourierAvailabilityAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateCourierAvailabilityAsync_EmptyGuid_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpdateCourierAvailabilityAsync(Guid.Empty, true));
        Assert.Equal("courierId", ex.ParamName);
    }

    [Fact]
    public async Task UpdateCourierAvailabilityAsync_CourierNotFound_ThrowsInvalidOperationException()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Courier?)null);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateCourierAvailabilityAsync(id, false));
        Assert.Contains(id.ToString(), ex.Message);
    }

    [Fact]
    public async Task UpdateCourierAvailabilityAsync_UpdateFails_ReturnsFalse()
    {
        var id      = Guid.NewGuid();
        var courier = BuildCourier(id);
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(courier);
        _repo.Setup(r => r.UpdateAsync(courier)).ReturnsAsync((Courier?)null);

        var result = await _sut.UpdateCourierAvailabilityAsync(id, false);

        Assert.False(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateCourierAvailabilityAsync_ValidCourier_SetsAvailabilityAndReturnsTrue(bool isAvailable)
    {
        var id      = Guid.NewGuid();
        var courier = BuildCourier(id);
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(courier);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<Courier>())).ReturnsAsync(courier);

        var result = await _sut.UpdateCourierAvailabilityAsync(id, isAvailable);

        Assert.True(result);
        Assert.Equal(isAvailable, courier.IsAvailable);
    }

    // -----------------------------------------------------------------------
    //  UpdateCourierRatingAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateCourierRatingAsync_EmptyGuid_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpdateCourierRatingAsync(Guid.Empty, 5.0f));
        Assert.Equal("courierId", ex.ParamName);
    }

    [Fact]
    public async Task UpdateCourierRatingAsync_CourierNotFound_ThrowsInvalidOperationException()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Courier?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateCourierRatingAsync(id, 4.0f));
    }

    [Fact]
    public async Task UpdateCourierRatingAsync_UpdateFails_ReturnsFalse()
    {
        var id      = Guid.NewGuid();
        var courier = BuildCourier(id, ratingCount: 1, rating: 4.0f);
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(courier);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<Courier>())).ReturnsAsync((Courier?)null);

        var result = await _sut.UpdateCourierRatingAsync(id, 3.0f);

        Assert.False(result);
    }

    /// <summary>Branch: RatingCount == 0 → rating is set directly (no averaging).</summary>
    [Fact]
    public async Task UpdateCourierRatingAsync_FirstRating_SetsRatingDirectly()
    {
        var id      = Guid.NewGuid();
        var courier = BuildCourier(id, ratingCount: 0, rating: 0f);
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(courier);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<Courier>())).ReturnsAsync(courier);

        await _sut.UpdateCourierRatingAsync(id, 4.8f);

        Assert.Equal(4.8f, courier.Rating,      precision: 4);
        Assert.Equal(1,    courier.RatingCount);
    }

    /// <summary>Branch: RatingCount > 0 → weighted average is computed.</summary>
    [Fact]
    public async Task UpdateCourierRatingAsync_SubsequentRating_ComputesWeightedAverage()
    {
        var id = Guid.NewGuid();
        // existing: rating=4.0, count=2  → new average with 5.0 = (4.0*2+5.0)/3 ≈ 4.333
        var courier = BuildCourier(id, ratingCount: 2, rating: 4.0f);
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(courier);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<Courier>())).ReturnsAsync(courier);

        var result = await _sut.UpdateCourierRatingAsync(id, 5.0f);

        Assert.True(result);
        Assert.Equal(3,      courier.RatingCount);
        Assert.Equal(4.333f, courier.Rating, precision: 3);
    }

    // -----------------------------------------------------------------------
    //  UpdateCourierLocationAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateCourierLocationAsync_EmptyGuid_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.UpdateCourierLocationAsync(Guid.Empty, 10.0, 20.0));
        Assert.Equal("courierId", ex.ParamName);
    }

    [Fact]
    public async Task UpdateCourierLocationAsync_CourierNotFound_ThrowsInvalidOperationException()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync((Courier?)null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateCourierLocationAsync(id, 1.0, 2.0));
    }

    [Fact]
    public async Task UpdateCourierLocationAsync_UpdateFails_ReturnsFalse()
    {
        var id      = Guid.NewGuid();
        var courier = BuildCourier(id);
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(courier);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<Courier>())).ReturnsAsync((Courier?)null);

        var result = await _sut.UpdateCourierLocationAsync(id, 10.0, 20.0);

        Assert.False(result);
    }

    [Theory]
    [InlineData(0.0,    0.0)]
    [InlineData(90.0,   180.0)]
    [InlineData(-90.0, -180.0)]
    [InlineData(48.45,  34.98)]
    public async Task UpdateCourierLocationAsync_ValidCoordinates_UpdatesAndReturnsTrue(double lat, double lon)
    {
        var id      = Guid.NewGuid();
        var courier = BuildCourier(id);
        _repo.Setup(r => r.GetByIdAsync(id)).ReturnsAsync(courier);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<Courier>())).ReturnsAsync(courier);

        var result = await _sut.UpdateCourierLocationAsync(id, lat, lon);

        Assert.True(result);
        Assert.Equal(lat, courier.CurrentLocation.Latitude);
        Assert.Equal(lon, courier.CurrentLocation.Longitude);
    }

    // -----------------------------------------------------------------------
    //  AssignOrderToCourierAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AssignOrderToCourierAsync_EmptyOrderId_ThrowsArgumentException()
    {
        var order = new Order { Id = Guid.Empty };
        var ex    = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.AssignOrderToCourierAsync(order));
        Assert.Contains("Order ID cannot be empty", ex.Message);
    }

    [Fact]
    public async Task AssignOrderToCourierAsync_AlreadyAssignedCourier_ReturnsExistingCourierId()
    {
        var existingCourierId = Guid.NewGuid();
        var order = new Order { Id = Guid.NewGuid(), CourierId = existingCourierId };

        var result = await _sut.AssignOrderToCourierAsync(order);

        Assert.Equal(existingCourierId, result);
        _repo.Verify(r => r.GetAvailableCouriersAsync(), Times.Never);
    }

    [Fact]
    public async Task AssignOrderToCourierAsync_NoAvailableCouriers_ThrowsInvalidOperationException()
    {
        var order = new Order { Id = Guid.NewGuid(), CourierId = null };
        _repo.Setup(r => r.GetAvailableCouriersAsync())
             .ReturnsAsync(new List<Courier>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AssignOrderToCourierAsync(order));
        Assert.Contains("No available couriers", ex.Message);
    }

    [Fact]
    public async Task AssignOrderToCourierAsync_AvailableCouriers_AssignsFirstAndSendsMessage()
    {
        var courierId = Guid.NewGuid();
        var courier   = BuildCourier(courierId);
        var order     = new Order { Id = Guid.NewGuid(), CourierId = null };

        _repo.Setup(r => r.GetAvailableCouriersAsync())
             .ReturnsAsync(new List<Courier> { courier });
        _repo.Setup(r => r.GetByIdAsync(courierId)).ReturnsAsync(courier);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<Courier>())).ReturnsAsync(courier);

        var mockSendEndpoint = new Mock<ISendEndpoint>();
        mockSendEndpoint
            .Setup(e => e.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _endpointProvider
            .Setup(p => p.GetSendEndpoint(It.IsAny<Uri>()))
            .ReturnsAsync(mockSendEndpoint.Object);

        var assignedId = await _sut.AssignOrderToCourierAsync(order);

        Assert.Equal(courierId, assignedId);
        Assert.Equal(courierId, order.CourierId);
        mockSendEndpoint.Verify(
            e => e.Send(It.Is<Order>(o => o.Id == order.Id), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AssignOrderToCourierAsync_SendEndpointThrows_WrapsExceptionAsInvalidOperation()
    {
        var courierId = Guid.NewGuid();
        var courier   = BuildCourier(courierId);
        var order     = new Order { Id = Guid.NewGuid(), CourierId = null };

        _repo.Setup(r => r.GetAvailableCouriersAsync())
             .ReturnsAsync(new List<Courier> { courier });
        _repo.Setup(r => r.GetByIdAsync(courierId)).ReturnsAsync(courier);
        _repo.Setup(r => r.UpdateAsync(It.IsAny<Courier>())).ReturnsAsync(courier);

        _endpointProvider
            .Setup(p => p.GetSendEndpoint(It.IsAny<Uri>()))
            .ThrowsAsync(new Exception("broker unavailable"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.AssignOrderToCourierAsync(order));
        Assert.Contains("Failed to update courier availability", ex.Message);
        Assert.IsType<Exception>(ex.InnerException);
    }

    // -----------------------------------------------------------------------
    //  Private helpers
    // -----------------------------------------------------------------------

    private static Courier BuildCourier(
        Guid?   id          = null,
        int     ratingCount = 0,
        float   rating      = 4.5f,
        bool    isAvailable = true) => new Courier
    {
        Id              = id ?? Guid.NewGuid(),
        FirstName       = "Alpha",
        LastName        = "Beta",
        VehicleType     = VehicleType.Bike,
        IsAvailable     = isAvailable,
        Rating          = rating,
        RatingCount     = ratingCount,
        CurrentLocation = new GeoPoint(0, 0)
    };

    private static CourierRegistrationDto BuildRegistrationDto(
        string firstName   = "Test",
        string lastName    = "Courier",
        string vehicleType = "Foot") => new CourierRegistrationDto
    {
        FirstName   = firstName,
        LastName    = lastName,
        VehicleType = vehicleType
    };
}


// ===========================================================================
//  PART 2 — CourierServiceWithCache unit tests
// ===========================================================================

[Trait("Category", "Unit")]
public class CourierServiceWithCacheTests
{
    private readonly Mock<ICourierService>                    _innerService;
    private readonly Mock<IDatabase>                          _db;
    private readonly Mock<IConnectionMultiplexer>             _multiplexer;
    private readonly Mock<ILogger<CourierServiceWithCache>>   _logger;
    private readonly Tracer                                   _tracer;
    private readonly CourierServiceWithCache                  _sut;

    public CourierServiceWithCacheTests()
    {
        _innerService = new Mock<ICourierService>();
        _db           = new Mock<IDatabase>();
        _multiplexer  = new Mock<IConnectionMultiplexer>();
        _logger       = new Mock<ILogger<CourierServiceWithCache>>();
        _tracer       = Sdk.CreateTracerProviderBuilder().Build().GetTracer("UnitTest");

        _multiplexer.Setup(m => m.IsConnected).Returns(true);
        _multiplexer
            .Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(_db.Object);

        _db.Setup(db => db.StringSetAsync(
                It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan>(), It.IsAny<bool>(),
                It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        _sut = new CourierServiceWithCache(
            _innerService.Object, _multiplexer.Object, _logger.Object, _tracer);
    }

    // ── Constructor ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullCourierService_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new CourierServiceWithCache(null!, _multiplexer.Object, _logger.Object, _tracer));
        Assert.Equal("courierService", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new CourierServiceWithCache(_innerService.Object, _multiplexer.Object, null!, _tracer));
        Assert.Equal("logger", ex.ParamName);
    }

    [Fact]
    public void Constructor_DisconnectedRedis_ThrowsInvalidOperationException()
    {
        var disconnected = new Mock<IConnectionMultiplexer>();
        disconnected.Setup(m => m.IsConnected).Returns(false);

        Assert.Throws<InvalidOperationException>(() =>
            new CourierServiceWithCache(_innerService.Object, disconnected.Object, _logger.Object, _tracer));
    }

    // ── GetCourierLocationAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetCourierLocationAsync_EmptyGuid_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.GetCourierLocationAsync(Guid.Empty));
        Assert.Equal("courierId", ex.ParamName);
    }

    [Fact]
    public async Task GetCourierLocationAsync_CacheHit_ReturnsDeserializedLocationWithoutCallingInner()
    {
        var id       = Guid.NewGuid();
        var geoPoint = new GeoPoint(48.45, 34.98);
        var json     = JsonSerializer.Serialize(geoPoint);

        _db.Setup(db => db.StringGetAsync(
                It.Is<RedisKey>(k => k == $"courier:{id}:location"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(json);

        var result = await _sut.GetCourierLocationAsync(id);

        Assert.NotNull(result);
        Assert.Equal(geoPoint.Latitude,  result!.Latitude);
        Assert.Equal(geoPoint.Longitude, result.Longitude);
        _innerService.Verify(s => s.GetCourierLocationAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task GetCourierLocationAsync_CacheMiss_CallsInnerAndCachesResult()
    {
        var id       = Guid.NewGuid();
        var geoPoint = new GeoPoint(50.0, 30.0);

        _db.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        _innerService.Setup(s => s.GetCourierLocationAsync(id)).ReturnsAsync(geoPoint);

        var result = await _sut.GetCourierLocationAsync(id);

        Assert.NotNull(result);
        Assert.Equal(geoPoint.Latitude, result!.Latitude);
        _innerService.Verify(s => s.GetCourierLocationAsync(id), Times.Once);
        _db.Verify(db => db.StringSetAsync(
            It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
            It.Is<TimeSpan?>(t => t != null),
            It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    // ── GetCourierByIdAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetCourierByIdAsync_EmptyGuid_ThrowsArgumentException()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _sut.GetCourierByIdAsync(Guid.Empty));
        Assert.Equal("courierId", ex.ParamName);
    }

    [Fact]
    public async Task GetCourierByIdAsync_CacheHit_ReturnsCachedDtoWithoutCallingInner()
    {
        var id  = Guid.NewGuid();
        var dto = new CourierReadDto
        {
            Id              = id,
            FullName        = "Cached Courier",
            VehicleType     = "Car",
            CurrentLocation = new GeoPoint(),
            IsAvailable     = true
        };
        var json = JsonSerializer.Serialize(dto);

        _db.Setup(db => db.StringGetAsync(
                It.Is<RedisKey>(k => k == $"courier:{id}"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(json);

        var result = await _sut.GetCourierByIdAsync(id);

        Assert.NotNull(result);
        Assert.Equal(id,               result!.Id);
        Assert.Equal("Cached Courier", result.FullName);
        _innerService.Verify(s => s.GetCourierByIdAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task GetCourierByIdAsync_CacheMissAndInnerReturnsNull_ReturnsNullWithoutCaching()
    {
        var id = Guid.NewGuid();
        _db.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        _innerService.Setup(s => s.GetCourierByIdAsync(id)).ReturnsAsync((CourierReadDto?)null);

        var result = await _sut.GetCourierByIdAsync(id);

        Assert.Null(result);
        _db.Verify(db => db.StringSetAsync(
            It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()),
            Times.Never);
    }

    [Fact]
    public async Task GetCourierByIdAsync_CacheMissAndInnerReturnsDto_CachesAndReturnsCourier()
    {
        var id  = Guid.NewGuid();
        var dto = new CourierReadDto
        {
            Id              = id,
            FullName        = "Fresh Courier",
            VehicleType     = "Foot",
            CurrentLocation = new GeoPoint()
        };
        _db.Setup(db => db.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);
        _innerService.Setup(s => s.GetCourierByIdAsync(id)).ReturnsAsync(dto);

        var result = await _sut.GetCourierByIdAsync(id);

        Assert.Equal(id, result!.Id);
        _db.Verify(db => db.StringSetAsync(
            It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()),
            Times.Once);
    }

    // ── Delegating write/mutate methods ─────────────────────────────────────

    [Fact]
    public async Task RegisterCourierAsync_DelegatesToInnerService()
    {
        var dto = new CourierRegistrationDto
            { FirstName = "An", LastName = "Bcd", VehicleType = "Bike" };
        var expected = new CourierReadDto
            { Id = Guid.NewGuid(), FullName = "An Bcd", VehicleType = "Bike", CurrentLocation = new GeoPoint() };
        _innerService.Setup(s => s.RegisterCourierAsync(dto)).ReturnsAsync(expected);

        var result = await _sut.RegisterCourierAsync(dto);

        Assert.Equal(expected.Id, result!.Id);
        _innerService.Verify(s => s.RegisterCourierAsync(dto), Times.Once);
    }

    [Fact]
    public async Task AssignOrderToCourierAsync_DelegatesToInnerService()
    {
        var order     = new Order { Id = Guid.NewGuid() };
        var courierId = Guid.NewGuid();
        _innerService.Setup(s => s.AssignOrderToCourierAsync(order)).ReturnsAsync(courierId);

        var result = await _sut.AssignOrderToCourierAsync(order);

        Assert.Equal(courierId, result);
        _innerService.Verify(s => s.AssignOrderToCourierAsync(order), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateCourierAvailabilityAsync_DelegatesToInnerService(bool isAvailable)
    {
        var id = Guid.NewGuid();
        _innerService.Setup(s => s.UpdateCourierAvailabilityAsync(id, isAvailable)).ReturnsAsync(true);

        var result = await _sut.UpdateCourierAvailabilityAsync(id, isAvailable);

        Assert.True(result);
        _innerService.Verify(s => s.UpdateCourierAvailabilityAsync(id, isAvailable), Times.Once);
    }

    [Fact]
    public async Task UpdateCourierRatingAsync_DelegatesToInnerService()
    {
        var id = Guid.NewGuid();
        _innerService.Setup(s => s.UpdateCourierRatingAsync(id, 4.5f)).ReturnsAsync(true);

        var result = await _sut.UpdateCourierRatingAsync(id, 4.5f);

        Assert.True(result);
        _innerService.Verify(s => s.UpdateCourierRatingAsync(id, 4.5f), Times.Once);
    }

    [Fact]
    public async Task UpdateCourierLocationAsync_DelegatesToInnerService()
    {
        var id = Guid.NewGuid();
        _innerService.Setup(s => s.UpdateCourierLocationAsync(id, 1.0, 2.0)).ReturnsAsync(true);

        var result = await _sut.UpdateCourierLocationAsync(id, 1.0, 2.0);

        Assert.True(result);
        _innerService.Verify(s => s.UpdateCourierLocationAsync(id, 1.0, 2.0), Times.Once);
    }
}


// ===========================================================================
//  PART 3 — OrderConsumer unit tests
// ===========================================================================

[Trait("Category", "Unit")]
public class OrderConsumerTests
{
    private readonly Mock<ICourierService>        _courierService;
    private readonly Mock<ILogger<OrderConsumer>> _logger;
    private readonly Tracer                       _tracer;
    private readonly OrderConsumer                _sut;

    public OrderConsumerTests()
    {
        _courierService = new Mock<ICourierService>();
        _logger         = new Mock<ILogger<OrderConsumer>>();
        _tracer         = Sdk.CreateTracerProviderBuilder().Build().GetTracer("UnitTest");
        _sut            = new OrderConsumer(_courierService.Object, _logger.Object, _tracer);
    }

    [Fact]
    public void Constructor_NullCourierService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new OrderConsumer(null!, _logger.Object, _tracer));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new OrderConsumer(_courierService.Object, null!, _tracer));
    }

    [Fact]
    public async Task Consume_OrderAlreadyHasCourier_ReturnsEarlyWithoutAssigning()
    {
        // A non-empty CourierId triggers the early-return branch
        var order = new Order { Id = Guid.NewGuid(), CourierId = Guid.NewGuid(), Status = OrderStatus.Preparing };

        await _sut.Consume(BuildConsumeContext(order));

        _courierService.Verify(s => s.AssignOrderToCourierAsync(It.IsAny<Order>()), Times.Never);
    }

    [Theory]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.ReadyForPickup)]
    public async Task Consume_PreparingOrReadyForPickupWithNoCourier_AssignsCourier(OrderStatus status)
    {
        var courierId = Guid.NewGuid();
        var order     = new Order { Id = Guid.NewGuid(), CourierId = null, Status = status };
        _courierService.Setup(s => s.AssignOrderToCourierAsync(order)).ReturnsAsync(courierId);

        await _sut.Consume(BuildConsumeContext(order));

        _courierService.Verify(s => s.AssignOrderToCourierAsync(order), Times.Once);
    }

    [Theory]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.ReadyForPickup)]
    public async Task Consume_AssignmentThrows_PropagatesException(OrderStatus status)
    {
        var order = new Order { Id = Guid.NewGuid(), CourierId = null, Status = status };
        _courierService.Setup(s => s.AssignOrderToCourierAsync(order))
            .ThrowsAsync(new InvalidOperationException("no couriers"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.Consume(BuildConsumeContext(order)));
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task Consume_DeliveredOrCancelled_WithCourier_MarksAvailable(OrderStatus status)
    {
        var courierId = Guid.NewGuid();
        var order     = new Order { Id = Guid.NewGuid(), CourierId = courierId, Status = status };
        _courierService.Setup(s => s.UpdateCourierAvailabilityAsync(courierId, true)).ReturnsAsync(true);

        await _sut.Consume(BuildConsumeContext(order));

        _courierService.Verify(s => s.UpdateCourierAvailabilityAsync(courierId, true), Times.Once);
    }

    [Theory]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task Consume_DeliveredOrCancelled_WithoutCourier_DoesNotCallUpdateAvailability(OrderStatus status)
    {
        var order = new Order { Id = Guid.NewGuid(), CourierId = null, Status = status };

        await _sut.Consume(BuildConsumeContext(order));

        _courierService.Verify(
            s => s.UpdateCourierAvailabilityAsync(It.IsAny<Guid>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.InDelivery)]
    public async Task Consume_OtherStatuses_DoNotAssignOrUpdateAvailability(OrderStatus status)
    {
        var order = new Order { Id = Guid.NewGuid(), CourierId = null, Status = status };

        await _sut.Consume(BuildConsumeContext(order));

        _courierService.Verify(s => s.AssignOrderToCourierAsync(It.IsAny<Order>()),                       Times.Never);
        _courierService.Verify(s => s.UpdateCourierAvailabilityAsync(It.IsAny<Guid>(), It.IsAny<bool>()), Times.Never);
    }

    private static ConsumeContext<Order> BuildConsumeContext(Order order)
    {
        var ctx = new Mock<ConsumeContext<Order>>();
        ctx.Setup(c => c.Message).Returns(order);
        return ctx.Object;
    }
}


// ===========================================================================
//  PART 4 — RestaurantOrderConsumer unit tests
// ===========================================================================

[Trait("Category", "Unit")]
public class RestaurantOrderConsumerTests
{
    private readonly Mock<ICourierService>                  _courierService;
    private readonly Mock<ILogger<RestaurantOrderConsumer>> _logger;
    private readonly Tracer                                 _tracer;
    private readonly RestaurantOrderConsumer                _sut;

    public RestaurantOrderConsumerTests()
    {
        _courierService = new Mock<ICourierService>();
        _logger         = new Mock<ILogger<RestaurantOrderConsumer>>();
        _tracer         = Sdk.CreateTracerProviderBuilder().Build().GetTracer("UnitTest");
        _sut            = new RestaurantOrderConsumer(_courierService.Object, _logger.Object, _tracer);
    }

    [Fact]
    public void Constructor_NullCourierService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new RestaurantOrderConsumer(null!, _logger.Object, _tracer));
    }

    [Fact]
    public async Task Consume_OrderAlreadyHasCourier_ReturnsEarlyWithoutAssigning()
    {
        var order = new Order { Id = Guid.NewGuid(), CourierId = Guid.NewGuid(), Status = OrderStatus.Confirmed };
        await _sut.Consume(BuildConsumeContext(order));
        _courierService.Verify(s => s.AssignOrderToCourierAsync(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task Consume_ConfirmedOrderWithNoCourier_AssignsCourier()
    {
        var courierId = Guid.NewGuid();
        var order     = new Order { Id = Guid.NewGuid(), CourierId = null, Status = OrderStatus.Confirmed };
        _courierService.Setup(s => s.AssignOrderToCourierAsync(order)).ReturnsAsync(courierId);

        await _sut.Consume(BuildConsumeContext(order));

        _courierService.Verify(s => s.AssignOrderToCourierAsync(order), Times.Once);
    }

    [Fact]
    public async Task Consume_ConfirmedOrder_AssignmentThrows_PropagatesException()
    {
        var order = new Order { Id = Guid.NewGuid(), CourierId = null, Status = OrderStatus.Confirmed };
        _courierService.Setup(s => s.AssignOrderToCourierAsync(order))
            .ThrowsAsync(new InvalidOperationException("failure"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.Consume(BuildConsumeContext(order)));
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.ReadyForPickup)]
    [InlineData(OrderStatus.InDelivery)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task Consume_NonConfirmedStatuses_DoNotAssign(OrderStatus status)
    {
        var order = new Order { Id = Guid.NewGuid(), CourierId = null, Status = status };
        await _sut.Consume(BuildConsumeContext(order));
        _courierService.Verify(s => s.AssignOrderToCourierAsync(It.IsAny<Order>()), Times.Never);
    }

    private static ConsumeContext<Order> BuildConsumeContext(Order order)
    {
        var ctx = new Mock<ConsumeContext<Order>>();
        ctx.Setup(c => c.Message).Returns(order);
        return ctx.Object;
    }
}


// ===========================================================================
//  PART 5 — EnumValidation attribute unit tests
// ===========================================================================

[Trait("Category", "Unit")]
public class EnumValidationAttributeTests
{
    private readonly EnumValidation _sut = new(typeof(VehicleType));

    [Fact]
    public void IsValid_NullValue_ReturnsSuccess()
    {
        var result = _sut.GetValidationResult(null, new ValidationContext(new object()));
        Assert.Equal(DataAnnotationsValidationResult.Success, result);
    }

    [Theory]
    [InlineData("Foot")]
    [InlineData("foot")]
    [InlineData("BIKE")]
    [InlineData("Scooter")]
    [InlineData("Car")]
    public void IsValid_ValidEnumString_ReturnsSuccess(string value)
    {
        var result = _sut.GetValidationResult(value, new ValidationContext(new object()));
        Assert.Equal(DataAnnotationsValidationResult.Success, result);
    }

    [Theory]
    [InlineData("Helicopter")]
    [InlineData("123")]
    public void IsValid_InvalidEnumString_ReturnsValidationError(string value)
    {
        var result = _sut.GetValidationResult(value, new ValidationContext(new object()));
        Assert.NotEqual(DataAnnotationsValidationResult.Success, result);
        Assert.Contains(value, result!.ErrorMessage);
    }

    [Fact]
    public void IsValid_NonEnumType_ThrowsArgumentException()
    {
        var badAttribute = new EnumValidation(typeof(string));
        Assert.Throws<ArgumentException>(() =>
            badAttribute.GetValidationResult("anything", new ValidationContext(new object())));
    }
}


// ===========================================================================
//  PART 6 — AutoMapper CourierProfile unit tests
// ===========================================================================

[Trait("Category", "Unit")]
public class CourierProfileMappingTests
{
    private readonly IMapper _mapper =
        new MapperConfiguration(cfg => cfg.AddProfile<CourierProfile>()).CreateMapper();

    [Fact]
    public void MapperConfiguration_IsValid()
    {
        _mapper.ConfigurationProvider.AssertConfigurationIsValid();
    }

    [Fact]
    public void Map_CourierToReadDto_FullNameConcatenatesFirstAndLastName()
    {
        var courier = new Courier
        {
            Id = Guid.NewGuid(), FirstName = "Olena", LastName = "Kovalenko",
            VehicleType = VehicleType.Car, CurrentLocation = new GeoPoint(48, 37),
            IsAvailable = true, Rating = 4.789f, RatingCount = 10
        };

        var dto = _mapper.Map<CourierReadDto>(courier);

        Assert.Equal("Olena Kovalenko", dto.FullName);
    }

    [Fact]
    public void Map_CourierToReadDto_RatingIsRoundedToTwoDecimalPlaces()
    {
        var courier = new Courier
        {
            Id = Guid.NewGuid(), FirstName = "An", LastName = "Bcd",
            VehicleType = VehicleType.Bike, CurrentLocation = new GeoPoint(),
            Rating = 4.789f
        };

        var dto = _mapper.Map<CourierReadDto>(courier);

        Assert.Equal(Math.Round(4.789f, 2), dto.Rating, precision: 5);
    }

    [Theory]
    [InlineData(VehicleType.Foot,    "Foot")]
    [InlineData(VehicleType.Bike,    "Bike")]
    [InlineData(VehicleType.Scooter, "Scooter")]
    [InlineData(VehicleType.Car,     "Car")]
    public void Map_CourierToReadDto_VehicleTypeIsConvertedToString(VehicleType type, string expected)
    {
        var courier = new Courier
        {
            Id = Guid.NewGuid(), FirstName = "Xx", LastName = "Yzw",
            VehicleType = type, CurrentLocation = new GeoPoint()
        };

        var dto = _mapper.Map<CourierReadDto>(courier);

        Assert.Equal(expected, dto.VehicleType);
    }

    [Theory]
    [InlineData("Foot",    VehicleType.Foot)]
    [InlineData("BIKE",    VehicleType.Bike)]
    [InlineData("scooter", VehicleType.Scooter)]
    [InlineData("Car",     VehicleType.Car)]
    public void Map_RegistrationDtoToCourier_VehicleTypeIsParsedCaseInsensitively(string vehicleStr, VehicleType expectedEnum)
    {
        var dto = new CourierRegistrationDto
            { FirstName = "Te", LastName = "Ste", VehicleType = vehicleStr };

        var entity = _mapper.Map<Courier>(dto);

        Assert.Equal(expectedEnum, entity.VehicleType);
    }

    [Fact]
    public void Map_RegistrationDtoToCourier_GeneratesNonEmptyId()
    {
        var dto = new CourierRegistrationDto
            { FirstName = "Na", LastName = "Mee", VehicleType = "Foot" };

        var entity = _mapper.Map<Courier>(dto);

        Assert.NotEqual(Guid.Empty, entity.Id);
    }
}
