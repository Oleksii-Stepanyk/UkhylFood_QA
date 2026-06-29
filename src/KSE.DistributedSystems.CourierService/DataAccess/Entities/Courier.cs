using System;

namespace KSE.DistributedSystems.CourierService.DataAccess.Entities;

public class Courier
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = default!;
    public string LastName { get; set; } = default!;
    public VehicleType VehicleType { get; set; }
    public GeoPoint CurrentLocation { get; set; } = new GeoPoint();
    public bool IsAvailable { get; set; } = true;
    public float Rating { get; set; }
    public int RatingCount { get; set; }
}

public class GeoPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public GeoPoint() { }

    public GeoPoint(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }
}

public enum VehicleType
{
    Foot,
    Bike,
    Scooter,
    Car
}