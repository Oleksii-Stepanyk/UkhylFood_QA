using System;
using System.Collections.Generic;

namespace KSE.DistributedSystems.RestaurantService.API.DTOs;

public class PaginatedResultWithFilters<T> : PaginatedResult<T>
{
    public Guid RestaurantId { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    
    public PaginatedResultWithFilters(List<T> items, int offset, int limit, int totalCount, Guid restaurantId, DateTime from, DateTime to) : base(items, offset, limit, totalCount)
    {
        RestaurantId = restaurantId;
        From = from;
        To = to;
    }
}