using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AutoMapper;
using KSE.DistributedSystems.CourierService.BusinessLogic.DTOs;
using KSE.DistributedSystems.CourierService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.CourierService.DataAccess.Entities;
using KSE.DistributedSystems.CourierService.DataAccess.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Order = KSE.DistributedSystems.OrderService.Models.Order;

namespace KSE.DistributedSystems.CourierService.BusinessLogic.Services;

public class CourierService : ICourierService
{
    private readonly ICourierRepository _repository;
    private readonly IMapper _mapper;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly ILogger<CourierService> _logger;
    private readonly Tracer _tracer;

    public CourierService(ICourierRepository repository, IMapper mapper, ISendEndpointProvider sendEndpointProvider,
        ILogger<CourierService> logger, Tracer tracer)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _sendEndpointProvider = sendEndpointProvider ?? throw new ArgumentNullException(nameof(sendEndpointProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    }

    public async Task<CourierReadDto?> GetCourierByIdAsync(Guid courierId)
    {
        using var span = _tracer.StartActiveSpan("GetCourierByIdAsync");

        if (courierId == Guid.Empty)
        {
            span.AddEvent("Invalid courier ID provided.");
            span.SetStatus(Status.Error.WithDescription("Courier ID cannot be empty."));

            _logger.LogError("Courier ID cannot be empty.");
            throw new ArgumentException("Courier ID cannot be empty.", nameof(courierId));
        }

        _logger.LogDebug("Fetching courier with ID: {CourierId}", courierId);

        span.AddEvent($"Fetching courier with ID: {courierId}");
        var courier = await _repository.GetByIdAsync(courierId);

        if (courier != null)
        {
            _logger.LogInformation("Courier with ID {CourierId} found.", courierId);

            span.AddEvent($"Courier with ID {courierId} found.");
            span.SetStatus(Status.Ok);
            return _mapper.Map<CourierReadDto>(courier);
        }

        _logger.LogWarning("Courier with ID {CourierId} not found.", courierId);

        span.AddEvent($"Courier with ID {courierId} not found.");
        span.SetStatus(Status.Ok);
        return null;
    }

    public async Task<GeoPoint?> GetCourierLocationAsync(Guid courierId)
    {
        using var span = _tracer.StartActiveSpan("GetCourierLocationAsync");

        if (courierId == Guid.Empty)
        {
            span.AddEvent("Invalid courier ID provided.");
            span.SetStatus(Status.Error.WithDescription("Courier ID cannot be empty."));

            _logger.LogError("Courier ID cannot be empty.");
            throw new ArgumentException("Courier ID cannot be empty.", nameof(courierId));
        }

        _logger.LogDebug("Fetching location for courier with ID: {CourierId}", courierId);

        var location = await _repository.GetCourierLocationAsync(courierId);

        if (location == null)
        {
            span.AddEvent($"Courier with ID {courierId} has no location.");
            span.SetStatus(Status.Ok);

            _logger.LogWarning("Courier with ID {CourierId} has no location.", courierId);
            return null;
        }

        span.AddEvent($"Returning courier location for courier ID: {courierId}");
        span.SetStatus(Status.Ok);

        _logger.LogInformation("Courier location for ID {CourierId} retrieved successfully.", courierId);
        return location;
    }

    public async Task<CourierReadDto?> RegisterCourierAsync(CourierRegistrationDto registrationDto)
    {
        using var span = _tracer.StartActiveSpan("RegisterCourierAsync");

        _logger.LogDebug("Registering new courier with provided DTO.");
        if (registrationDto == null)
        {
            span.AddEvent("Courier registration DTO is null.");
            span.SetStatus(Status.Error.WithDescription("Courier registration data cannot be null."));

            _logger.LogError("Courier registration data cannot be null.");
            throw new ArgumentNullException(nameof(registrationDto), "Courier registration data cannot be null.");
        }

        span.AddEvent("Mapping CourierRegistrationDto to Courier entity.");
        var courier = _mapper.Map<Courier>(registrationDto);

        if (courier == null)
        {
            span.AddEvent("Failed to map CourierRegistrationDto to Courier entity.");
            span.SetStatus(Status.Error.WithDescription("Failed to create courier from registration data."));

            _logger.LogError("Failed to create courier from registration data.");
            throw new InvalidOperationException("Failed to create courier from registration data.");
        }

        span.AddEvent("Adding new courier to the repository.");
        var resultCourier = await _repository.AddAsync(courier);

        if (resultCourier != null)
        {
            span.SetStatus(Status.Ok);

            _logger.LogInformation("Courier with ID {CourierId} registered successfully.", resultCourier.Id);
            return _mapper.Map<CourierReadDto>(resultCourier);
        }

        span.AddEvent("Failed to add courier to the repository.");
        span.SetStatus(Status.Error.WithDescription("Failed to register courier."));

        _logger.LogError("Failed to register courier.");
        throw new InvalidOperationException("Failed to register courier.");
    }

    public async Task<Guid> AssignOrderToCourierAsync(Order order)
    {
        using var span = _tracer.StartActiveSpan("AssignOrderToCourierAsync");

        _logger.LogDebug("Assigning order with ID {OrderId} to a courier.", order.Id);
        if (order.Id == Guid.Empty)
        {
            span.AddEvent("Invalid order ID provided.");
            span.SetStatus(Status.Error.WithDescription("Order ID cannot be empty."));

            _logger.LogError("Order ID cannot be empty.");
            throw new ArgumentException("Order ID cannot be empty.", nameof(order.Id));
        }

        if (order.CourierId != Guid.Empty && order.CourierId != null)
        {
            span.AddEvent($"Order {order.Id} is already assigned to a courier with ID {order.CourierId}.");
            _logger.LogInformation("Order {OrderId} is already assigned to courier {CourierId}.", order.Id,
                order.CourierId);
            return (Guid)order.CourierId;
        }

        span.AddEvent($"Fetching available couriers for order {order.Id}.");
        var availableCouriers = (await _repository.GetAvailableCouriersAsync()).ToList();

        if (availableCouriers == null || availableCouriers.Count == 0)
        {
            span.AddEvent($"No available couriers found for order {order.Id}.");
            span.SetStatus(Status.Error.WithDescription("No available couriers found."));

            _logger.LogError("No available couriers found for order {OrderId}.", order.Id);
            throw new InvalidOperationException("No available couriers found.");
        }

        span.AddEvent($"Assigning order {order.Id} to the first available courier.");
        var courier = availableCouriers.First();
        order.CourierId = courier.Id;

        try
        {
            span.AddEvent($"Updating courier availability for courier ID {courier.Id} to unavailable.");
            await UpdateCourierAvailabilityAsync(courier.Id, false);
            _logger.LogDebug("Updated courier availability for courier ID {CourierId} to unavailable.", courier.Id);

            span.AddEvent($"Sending order {order.Id} to the courier service queue.");
            var sendEndpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri("queue:courier.order"));
            await sendEndpoint.Send(order);
        }
        catch (Exception ex)
        {
            span.AddEvent($"Failed to update courier availability for courier ID {courier.Id}: {ex.Message}");
            span.SetStatus(Status.Error.WithDescription(ex.Message));

            _logger.LogError(ex, "Failed to update courier availability for courier ID {CourierId}.", courier.Id);
            throw new InvalidOperationException($"Failed to update courier availability: {ex.Message}", ex);
        }

        _logger.LogDebug("Order {OrderId} successfully assigned to courier ID {CourierId}.", order.Id, courier.Id);

        span.AddEvent($"Order {order.Id} successfully assigned to courier ID {courier.Id}.");
        span.SetStatus(Status.Ok);
        return courier.Id;
    }

    public async Task<bool> UpdateCourierAvailabilityAsync(Guid courierId, bool isAvailable)
    {
        using var span = _tracer.StartActiveSpan("UpdateCourierAvailabilityAsync");

        _logger.LogDebug("Updating availability for courier with ID: {CourierId}", courierId);
        if (courierId == Guid.Empty)
        {
            span.AddEvent("Invalid courier ID provided.");
            span.SetStatus(Status.Error.WithDescription("Courier ID cannot be empty."));

            _logger.LogError("Courier ID cannot be empty.");
            throw new ArgumentException("Courier ID cannot be empty.", nameof(courierId));
        }

        span.AddEvent($"Fetching courier with ID {courierId} to update availability.");
        var courier = await _repository.GetByIdAsync(courierId);

        if (courier == null)
        {
            span.AddEvent($"Courier with ID {courierId} not found.");
            span.SetStatus(Status.Error.WithDescription($"Courier with ID {courierId} not found."));

            _logger.LogError("Courier with ID {CourierId} not found.", courierId);
            throw new InvalidOperationException($"Courier with ID {courierId} not found.");
        }

        courier.IsAvailable = isAvailable;
        span.AddEvent($"Updating availability for courier ID {courierId} to {isAvailable}.");

        var updatedCourier = await _repository.UpdateAsync(courier);
        if (updatedCourier == null)
        {
            span.AddEvent($"Failed to update availability for courier ID {courierId}.");
            span.SetStatus(Status.Error.WithDescription("Failed to update courier availability."));

            _logger.LogError("Failed to update availability for courier ID {CourierId}.", courierId);
            return false;
        }

        _logger.LogInformation("Courier ID {CourierId} availability updated successfully to {IsAvailable}.", courierId,
            isAvailable);

        span.AddEvent($"Courier ID {courierId} availability updated successfully.");
        span.SetStatus(Status.Ok);
        return true;
    }

    public async Task<bool> UpdateCourierRatingAsync(Guid courierId, float rating)
    {
        using var span = _tracer.StartActiveSpan("UpdateCourierRatingAsync");

        _logger.LogDebug("Updating rating for courier with ID: {CourierId}", courierId);
        if (courierId == Guid.Empty)
        {
            span.AddEvent("Invalid courier ID provided.");
            span.SetStatus(Status.Error.WithDescription("Courier ID cannot be empty."));

            _logger.LogError("Courier ID cannot be empty.");
            throw new ArgumentException("Courier ID cannot be empty.", nameof(courierId));
        }

        span.AddEvent($"Fetching courier with ID {courierId} to update rating.");
        var courier = await _repository.GetByIdAsync(courierId);

        if (courier == null)
        {
            span.AddEvent($"Courier with ID {courierId} not found.");
            span.SetStatus(Status.Error.WithDescription($"Courier with ID {courierId} not found."));

            _logger.LogError("Courier with ID {CourierId} not found.", courierId);
            throw new InvalidOperationException($"Courier with ID {courierId} not found.");
        }

        span.AddEvent($"Updating rating for courier ID {courierId} with new rating {rating}.");
        if (courier.RatingCount == 0)
        {
            courier.Rating = rating;
        }
        else
        {
            courier.Rating = (courier.Rating * courier.RatingCount + rating) / (courier.RatingCount + 1);
        }

        _logger.LogDebug("Courier ID {CourierId} current rating: {CurrentRating}, new rating: {NewRating}.",
            courierId, courier.Rating, rating);
        courier.RatingCount++;

        span.AddEvent($"Incrementing rating count for courier ID {courierId} to {courier.RatingCount}.");
        var updatedCourier = await _repository.UpdateAsync(courier);
        if (updatedCourier == null)
        {
            span.AddEvent($"Failed to update rating for courier ID {courierId}.");
            span.SetStatus(Status.Error.WithDescription("Failed to update courier rating."));

            _logger.LogError("Failed to update rating for courier ID {CourierId}.", courierId);
            return false;
        }

        _logger.LogInformation("Courier ID {CourierId} rating updated successfully to {Rating}.", courierId,
            courier.Rating);
        span.AddEvent($"Courier ID {courierId} rating updated successfully to {courier.Rating}.");
        span.SetStatus(Status.Ok);
        return true;
    }

    public async Task<bool> UpdateCourierLocationAsync(Guid courierId, double latitude, double longitude)
    {
        using var span = _tracer.StartActiveSpan("UpdateCourierLocationAsync");

        _logger.LogDebug("Updating location for courier with ID: {CourierId}", courierId);
        if (courierId == Guid.Empty)
        {
            span.AddEvent("Invalid courier ID provided.");
            span.SetStatus(Status.Error.WithDescription("Courier ID cannot be empty."));

            _logger.LogError("Courier ID cannot be empty.");
            throw new ArgumentException("Courier ID cannot be empty.", nameof(courierId));
        }

        span.AddEvent($"Fetching courier with ID {courierId} to update location.");
        var courier = await _repository.GetByIdAsync(courierId);

        if (courier == null)
        {
            span.AddEvent($"Courier with ID {courierId} not found.");
            span.SetStatus(Status.Error.WithDescription($"Courier with ID {courierId} not found."));

            _logger.LogError("Courier with ID {CourierId} not found.", courierId);
            throw new InvalidOperationException($"Courier with ID {courierId} not found.");
        }

        span.AddEvent("Updating location for courier.");
        var location = courier.CurrentLocation;

        location.Latitude = latitude;
        location.Longitude = longitude;

        var updatedCourier = await _repository.UpdateAsync(courier);
        if (updatedCourier == null)
        {
            span.AddEvent($"Failed to update location for courier ID {courierId}.");
            span.SetStatus(Status.Error.WithDescription("Failed to update courier location."));

            _logger.LogError("Failed to update location for courier ID {CourierId}.", courierId);
            return false;
        }

        span.AddEvent($"Courier ID {courierId} location updated successfully to ({latitude}, {longitude}).");

        _logger.LogDebug("Courier ID {CourierId} location updated successfully to ({Latitude}, {Longitude}).",
            courierId, latitude, longitude);

        span.SetStatus(Status.Ok);
        return true;
    }
}