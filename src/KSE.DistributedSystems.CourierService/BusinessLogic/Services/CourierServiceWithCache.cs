using System;
using System.Text.Json;
using System.Threading.Tasks;
using KSE.DistributedSystems.CourierService.BusinessLogic.DTOs;
using KSE.DistributedSystems.CourierService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.CourierService.DataAccess.Entities;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using StackExchange.Redis;
using Order = KSE.DistributedSystems.OrderService.Models.Order;

namespace KSE.DistributedSystems.CourierService.BusinessLogic.Services;

public class CourierServiceWithCache : ICourierService
{
    private readonly ICourierService _courierService;
    private readonly IDatabase _db;
    private readonly ILogger<CourierServiceWithCache> _logger;
    private readonly Tracer _tracer;

    public CourierServiceWithCache(ICourierService courierService, IConnectionMultiplexer cache,
        ILogger<CourierServiceWithCache> logger, Tracer tracer)
    {
        _courierService = courierService ?? throw new ArgumentNullException(nameof(courierService));
        if (!cache.IsConnected)
        {
            throw new InvalidOperationException("Redis cache is not connected.");
        }

        _db = cache.GetDatabase();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    }

    public async Task<GeoPoint?> GetCourierLocationAsync(Guid courierId)
    {
        using var span = _tracer.StartActiveSpan("GetCourierLocationAsyncWithCache");

        if (courierId == Guid.Empty)
        {
            span.AddEvent("Invalid courier ID provided.");
            span.SetStatus(Status.Error.WithDescription("Courier ID cannot be empty."));

            _logger.LogError("Courier ID cannot be empty.");
            throw new ArgumentException("Courier ID cannot be empty.", nameof(courierId));
        }

        _logger.LogDebug("Checking cache for location of a courier with ID: {CourierId}", courierId);

        var cacheKey = $"courier:{courierId}:location";

        var cached = await _db.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            _logger.LogInformation("Courier location for ID {CourierId} found in cache.", courierId);
            return JsonSerializer.Deserialize<GeoPoint>(cached!);
        }

        _logger.LogInformation("Courier location for ID {CourierId} not found in cache, fetching from service.",
            courierId);
        var location = await _courierService.GetCourierLocationAsync(courierId);

        var serialized = JsonSerializer.Serialize(location);
        await _db.StringSetAsync(cacheKey, serialized, TimeSpan.FromMinutes(2));

        _logger.LogInformation("Courier location for ID {CourierId} cached successfully.", courierId);

        span.AddEvent("Courier location fetched and cached successfully.");
        span.SetStatus(Status.Ok);

        return location;
    }

    public async Task<CourierReadDto?> GetCourierByIdAsync(Guid courierId)
    {
        using var span = _tracer.StartActiveSpan("GetCourierByIdAsyncWithCache");

        if (courierId == Guid.Empty)
        {
            span.AddEvent("Invalid courier ID provided.");
            span.SetStatus(Status.Error.WithDescription("Courier ID cannot be empty."));

            _logger.LogError("Courier ID cannot be empty.");
            throw new ArgumentException("Courier ID cannot be empty.", nameof(courierId));
        }

        var cacheKey = $"courier:{courierId}";
        _logger.LogDebug("Checking cache for courier with ID: {CourierId}", courierId);

        var cached = await _db.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            _logger.LogInformation("Courier with ID {CourierId} found in cache.", courierId);
            return JsonSerializer.Deserialize<CourierReadDto>(cached!);
        }

        _logger.LogInformation("Courier with ID {CourierId} not found in cache, fetching from service.", courierId);
        var courier = await _courierService.GetCourierByIdAsync(courierId);
        if (courier == null)
        {
            _logger.LogWarning("Courier with ID {CourierId} not found in service.", courierId);
            return null;
        }

        var serialized = JsonSerializer.Serialize(courier);
        await _db.StringSetAsync(cacheKey, serialized, TimeSpan.FromMinutes(5));
        _logger.LogInformation("Courier with ID {CourierId} cached successfully.", courierId);

        span.AddEvent("Courier fetched and cached successfully.");
        span.SetStatus(Status.Ok);

        return courier;
    }

    public async Task<CourierReadDto?> RegisterCourierAsync(CourierRegistrationDto registrationDto) =>
        await _courierService.RegisterCourierAsync(registrationDto);

    public async Task<Guid> AssignOrderToCourierAsync(Order order) =>
        await _courierService.AssignOrderToCourierAsync(order);

    public async Task<bool> UpdateCourierRatingAsync(Guid courierId, float rating) =>
        await _courierService.UpdateCourierRatingAsync(courierId, rating);

    public async Task<bool> UpdateCourierLocationAsync(Guid courierId, double latitude, double longitude) =>
        await _courierService.UpdateCourierLocationAsync(courierId, latitude, longitude);

    public async Task<bool> UpdateCourierAvailabilityAsync(Guid courierId, bool isAvailable) =>
        await _courierService.UpdateCourierAvailabilityAsync(courierId, isAvailable);
}