using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KSE.DistributedSystems.CourierService.DataAccess.Entities;

namespace KSE.DistributedSystems.CourierService.DataAccess.Interfaces;

public interface ICourierRepository
{
    Task<Courier?> GetByIdAsync(Guid id);
    Task<GeoPoint?> GetCourierLocationAsync(Guid id);
    Task<IEnumerable<Courier>> GetAllAsync();
    Task<IEnumerable<Courier>> GetAvailableCouriersAsync();
    Task<IEnumerable<Courier>> GetCouriersByVehicleTypeAsync(VehicleType vehicleType);
    Task<Courier?> AddAsync(Courier courier);
    Task<Courier?> UpdateAsync(Courier courier);
    Task<bool> DeleteAsync(Guid id);
}