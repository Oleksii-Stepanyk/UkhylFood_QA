using System;

namespace KSE.DistributedSystems.RestaurantService.DataAccess.Models;

public class Item
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public required Restaurant Restaurant { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public float Price { get; set; }
    public bool IsAvailable { get; set; }
    public Category Category { get; set; }
}

public enum Category
{
    Main,
    Side,
    Drink,
    Dessert
}