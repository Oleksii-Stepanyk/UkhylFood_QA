using System.Collections.Generic;

namespace KSE.DistributedSystems.RestaurantService.API.DTOs;

public class PaginatedResult<T>
{
    public List<T> Items { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public int TotalCount { get; set; }
    
    public PaginatedResult(List<T> items, int offset, int limit, int totalCount)
    {
        Items = items;
        Offset = offset;
        Limit = limit;
        TotalCount = totalCount;
    }
}