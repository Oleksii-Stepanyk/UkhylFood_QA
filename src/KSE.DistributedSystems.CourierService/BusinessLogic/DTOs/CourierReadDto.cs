using System;
using KSE.DistributedSystems.CourierService.DataAccess.Entities;

namespace KSE.DistributedSystems.CourierService.BusinessLogic.DTOs;

public class CourierReadDto
{
    public Guid Id { get; set; }
    public required string FullName { get; set; }
    public required string VehicleType { get; set; }
    public required GeoPoint CurrentLocation { get; set; }
    public bool IsAvailable { get; set; }
    public double Rating { get; set; }
}