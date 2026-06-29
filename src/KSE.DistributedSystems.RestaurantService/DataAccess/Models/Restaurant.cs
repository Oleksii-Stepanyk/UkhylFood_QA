using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace KSE.DistributedSystems.RestaurantService.DataAccess.Models;

public class Restaurant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Location { get; set; } = null!;
    public List<Item> MenuItems { get; set; } = [];
    public TimeOnly OpensAt { get; set; }
    public TimeOnly ClosesAt { get; set; }

    [NotMapped]
    public bool IsOpen
    {
        get
        {
            var now = TimeOnly.FromDateTime(DateTime.UtcNow);
            return now > OpensAt && now < ClosesAt;
        }
    }
}