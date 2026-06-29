using System;
using System.Threading.Tasks;
using KSE.DistributedSystems.CourierService.BusinessLogic.DTOs;
using KSE.DistributedSystems.CourierService.DataAccess.Entities;
using KSE.DistributedSystems.OrderService.Models;

namespace KSE.DistributedSystems.CourierService.BusinessLogic.Interfaces;

public interface ICourierService
{
    Task<Guid> AssignOrderToCourierAsync(Order order);
    Task<CourierReadDto?> GetCourierByIdAsync(Guid courierId);
    Task<GeoPoint?> GetCourierLocationAsync(Guid courierId);
    Task<CourierReadDto?> RegisterCourierAsync(CourierRegistrationDto registrationDto);
    Task<bool> UpdateCourierRatingAsync(Guid courierId, float rating);
    Task<bool> UpdateCourierAvailabilityAsync(Guid courierId, bool isAvailable);
    Task<bool> UpdateCourierLocationAsync(Guid courierId, double latitude, double longitude);
}