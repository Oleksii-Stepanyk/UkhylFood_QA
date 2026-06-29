using System;
using System.Threading.Tasks;
using KSE.DistributedSystems.RestaurantService.API.DTOs;
using KSE.DistributedSystems.RestaurantService.Application.Services;
using Microsoft.AspNetCore.Mvc;
using KSE.DistributedSystems.RestaurantService.Application.Exceptions;
using Microsoft.Extensions.Logging;

namespace KSE.DistributedSystems.RestaurantService.API.Controllers;

[Route("api/orders")]
[ApiController]
public class OrderController : ControllerBase
{
    private readonly IOrderService _service;
    private readonly ILogger<OrderController> _logger;

    public OrderController(IOrderService service, ILogger<OrderController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetAllOrders([FromQuery] Guid restaurantId,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 10)
    {
        _logger.LogInformation("Retrieving orders for {RestaurantId} restaurant", restaurantId);
        try
        {
            PaginatedResult<OrderResponse> response =
                await _service.GetOrdersByRestaurantId(restaurantId, offset, limit);

            _logger.LogInformation("Retrieval successful");
            return Ok(response);
        }
        catch (StatusCodeBasedExceptions.NotFoundException e)
        {
            _logger.LogWarning(e, "NotFound exception was thrown at [{TimeStamp}]", DateTime.UtcNow.ToString("o"));
            return NotFound(e.Message);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById([FromRoute] Guid id)
    {
        _logger.LogInformation("Retrieving order with id {Id}", id);
        try
        {
            var order = await _service.GetOrderByIdAsync(id);
            
            return Ok(order);
        }
        catch (StatusCodeBasedExceptions.NotFoundException e)
        {
            _logger.LogWarning(e, "Not found exception raised");
            return NotFound(e.Message);
        }
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> UpdateOrderStatus([FromRoute] Guid id, [FromBody] UpdateOrderStatusRequest request)
    {
        if (id != request.OrderId)
        {
            _logger.LogWarning("Request ids mismatched. Route id: {RouteId}; Entity id: {EntityId}", id, request.OrderId);
            return BadRequest("Ids must match");
        }
            

        _logger.LogInformation("Updating order status for {OrderId}", request.OrderId);
        try
        {
            await _service.UpdateOrderStatusAsync(request);
            _logger.LogInformation("Updated successfully");
            return NoContent();
        }
        catch (StatusCodeBasedExceptions.BadRequestException e)
        {
            _logger.LogError(e, "Bad request exception raised");
            return BadRequest(e.Message);
        }
        catch (StatusCodeBasedExceptions.NotFoundException e)
        {
            _logger.LogError(e, "Not found exception raised");
            return NotFound(e.Message);
        }
    }
}