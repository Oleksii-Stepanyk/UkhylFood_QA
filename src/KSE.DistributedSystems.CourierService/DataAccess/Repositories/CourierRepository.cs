using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KSE.DistributedSystems.CourierService.DataAccess.Entities;
using KSE.DistributedSystems.CourierService.DataAccess.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace KSE.DistributedSystems.CourierService.DataAccess.Repositories;

public class CourierRepository : ICourierRepository
{
    private readonly CourierDbContext _context;
    private readonly ILogger<CourierRepository> _logger;
    private readonly Tracer _tracer;

    public CourierRepository(CourierDbContext context, ILogger<CourierRepository> logger, Tracer tracer)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context), "CourierDbContext cannot be null");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    }

    public async Task<Courier?> GetByIdAsync(Guid id)
    {
        using var span = _tracer.StartActiveSpan("GetCourierById");
        if (id == Guid.Empty)
        {
            span.AddEvent("Courier ID is empty");
            span.SetStatus(Status.Error.WithDescription("Invalid courier ID"));

            _logger.LogError("Attempted to fetch courier with an invalid ID: {CourierId}", id);
            throw new ArgumentException("Courier ID cannot be empty", nameof(id));
        }

        span.AddEvent($"Fetching courier with ID: {id}");

        _logger.LogInformation("Fetching courier with ID: {CourierId}", id);
        return await _context.Couriers
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<GeoPoint?> GetCourierLocationAsync(Guid id)
    {
        using var span = _tracer.StartActiveSpan("GetCourierLocationById");
        if (id == Guid.Empty)
        {
            span.AddEvent("Courier ID is empty");
            span.SetStatus(Status.Error.WithDescription("Invalid courier ID"));

            _logger.LogError("Attempted to fetch location for an invalid courier ID: {CourierId}", id);
            throw new ArgumentException("Courier ID cannot be empty", nameof(id));
        }

        span.AddEvent($"Fetching location for courier with ID: {id}");

        _logger.LogInformation("Fetching location for courier with ID: {CourierId}", id);
        var courier = await _context.Couriers
            .FirstOrDefaultAsync(c => c.Id == id);

        if (courier == null)
        {
            span.AddEvent("Courier not found");
            span.SetStatus(Status.Error.WithDescription("Courier not found"));

            _logger.LogWarning("Courier with ID {CourierId} not found", id);
            return null;
        }

        if (courier.CurrentLocation == null)
        {
            span.AddEvent("Courier has no current location");
            span.SetStatus(Status.Error.WithDescription("Courier has no current location"));

            _logger.LogWarning("Courier with ID {CourierId} has no current location", id);
            return null;
        }

        span.AddEvent($"Courier with ID: {id} has location: {courier.CurrentLocation}");
        span.SetStatus(Status.Ok);
        return courier.CurrentLocation;
    }

    public async Task<IEnumerable<Courier>> GetAllAsync()
    {
        using var span = _tracer.StartActiveSpan("GetAllCouriers");

        _logger.LogInformation("Fetching all couriers");
        return await _context.Couriers
            .ToListAsync();
    }

    public async Task<IEnumerable<Courier>> GetAvailableCouriersAsync()
    {
        using var span = _tracer.StartActiveSpan("GetAvailableCouriers");

        _logger.LogInformation("Fetching all available couriers");
        return await _context.Couriers
            .Where(c => c.IsAvailable)
            .ToListAsync();
    }

    public async Task<IEnumerable<Courier>> GetCouriersByVehicleTypeAsync(VehicleType vehicleType)
    {
        using var span = _tracer.StartActiveSpan("GetCouriersByVehicleType");
        if (!Enum.IsDefined(typeof(VehicleType), vehicleType))
        {
            span.AddEvent("Invalid vehicle type specified");
            span.SetStatus(Status.Error.WithDescription("Invalid vehicle type specified"));

            _logger.LogError("Invalid vehicle type: {VehicleType}", vehicleType);
            throw new ArgumentOutOfRangeException(nameof(vehicleType), "Invalid vehicle type specified");
        }

        span.AddEvent($"Fetching couriers with vehicle type: {vehicleType}");

        _logger.LogInformation("Fetching couriers with vehicle type: {VehicleType}", vehicleType);
        return await _context.Couriers
            .Where(c => c.VehicleType == vehicleType)
            .ToListAsync();
    }

    public async Task<Courier?> AddAsync(Courier courier)
    {
        using var span = _tracer.StartActiveSpan("AddCourier");
        if (courier == null)
        {
            span.AddEvent("Attempted to add a null courier");
            span.SetStatus(Status.Error.WithDescription("Courier cannot be null"));

            _logger.LogError("Attempted to add a null courier");
            throw new ArgumentNullException(nameof(courier), "Courier cannot be null");
        }

        span.AddEvent("Adding a new courier");

        await _context.Couriers.AddAsync(courier);
        var success = await _context.SaveChangesAsync() > 0;

        span.AddEvent($"Courier with ID: {courier.Id} added successfully: {success}");
        span.SetStatus(success ? Status.Ok : Status.Error.WithDescription("Failed to add courier"));

        _logger.LogInformation("Addition of courier with ID {CourierId} was {Status}", courier.Id,
            success ? "successful" : "unsuccessful");
        return courier;
    }

    public async Task<Courier?> UpdateAsync(Courier courier)
    {
        using var span = _tracer.StartActiveSpan("UpdateCourier");
        if (courier == null)
        {
            span.AddEvent("Attempted to update a null courier");
            span.SetStatus(Status.Error.WithDescription("Courier cannot be null"));

            _logger.LogError("Attempted to update a null courier");
            throw new ArgumentNullException(nameof(courier), "Courier cannot be null");
        }

        _logger.LogDebug("Updating courier with ID: {CourierId}", courier.Id);

        span.AddEvent("Updating courier");

        var fetchedCourier = await _context.Couriers.FindAsync(courier.Id);
        if (fetchedCourier != null)
        {
            span.AddEvent("Courier found for update");

            _context.Couriers.Update(courier);
            var success = await _context.SaveChangesAsync() > 0;

            span.AddEvent($"Courier with ID: {courier.Id} updated successfully: {success}");
            span.SetStatus(success ? Status.Ok : Status.Error.WithDescription("Failed to update courier"));

            _logger.LogInformation("Update of courier with ID {CourierId} was {Status}", courier.Id,
                success ? "successful" : "unsuccessful");
            return !success ? null : courier;
        }

        span.AddEvent("Courier not found for update");
        span.SetStatus(Status.Error.WithDescription("Courier not found for update"));

        _logger.LogWarning("Courier with ID {CourierId} not found for update", courier.Id);
        return null;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        using var span = _tracer.StartActiveSpan("DeleteCourier");
        if (id == Guid.Empty)
        {
            span.AddEvent("Courier ID is empty");
            span.SetStatus(Status.Error.WithDescription("Invalid courier ID"));

            _logger.LogError("Attempted to delete courier with an invalid ID: {CourierId}", id);
            throw new ArgumentException("Courier ID cannot be empty", nameof(id));
        }

        span.AddEvent("Deleting courier");

        var courier = await _context.Couriers.FindAsync(id);
        if (courier != null)
        {
            span.AddEvent($"Courier found for deletion with ID: {id}");

            _context.Couriers.Remove(courier);
            _logger.LogInformation("Deleting courier with ID: {CourierId}", id);
            return await _context.SaveChangesAsync() > 0;
        }

        span.AddEvent("Courier not found for deletion");
        span.SetStatus(Status.Error.WithDescription("Courier not found for deletion"));

        _logger.LogWarning("Courier with ID {CourierId} not found for deletion", id);
        return false;
    }
}