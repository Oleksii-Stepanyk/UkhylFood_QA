using System;
using System.Threading.Tasks;
using KSE.DistributedSystems.CourierService.BusinessLogic.DTOs;
using KSE.DistributedSystems.CourierService.BusinessLogic.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace KSE.DistributedSystems.CourierService.API.Controllers;

[ApiController]
[Route("[controller]")]
public class CouriersController : ControllerBase
{
    private readonly ICourierService _service;
    private readonly ILogger<CouriersController> _logger;

    public CouriersController(ICourierService service, ILogger<CouriersController> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger;
    }

    [HttpGet("{courierId:guid}")]
    public async Task<IActionResult> GetCourierById([FromRoute] Guid courierId)
    {
        _logger.LogDebug("GET Request to /couriers/id with courier ID: {CourierId}", courierId);
        if (courierId == Guid.Empty)
        {
            return BadRequest("Invalid courier ID.");
        }

        var courier = await _service.GetCourierByIdAsync(courierId);
        _logger.LogDebug("Courier retrieved: {@Courier}", courier);

        if (courier == null)
        {
            return NotFound($"Courier with ID {courierId} not found.");
        }

        return Ok(courier);
    }

    [HttpPost]
    public async Task<IActionResult> RegisterCourier([FromBody] CourierRegistrationDto registrationDto)
    {
        _logger.LogDebug("POST Request to /couriers with registration data: {@RegistrationDto}", registrationDto);
        if (registrationDto == null)
        {
            return BadRequest("Courier registration data is required.");
        }

        var courier = await _service.RegisterCourierAsync(registrationDto);
        _logger.LogDebug("Courier registration result: {@Courier}", courier);

        return courier == null
            ? StatusCode(500, "Failed to register courier.")
            : CreatedAtAction(nameof(GetCourierById), new { courierId = courier.Id }, courier);
    }

    // [HttpPost("assign")]
    // public async Task<IActionResult> AssignCourier([FromBody] CourierAssignOrderDto assignDto)
    // {
    //     if (assignDto.OrderId == Guid.Empty)
    //     {
    //         return BadRequest("Invalid order ID data.");
    //     }
    //
    //     var courierId = await _service.AssignOrderToCourierAsync(assignDto.OrderId);
    //
    //     return Ok(courierId);
    // }

    [HttpPatch("{courierId:guid}/availability")]
    public async Task<IActionResult> UpdateAvailability([FromRoute] Guid courierId,
        [FromBody] CourierAvailabilityUpdateDto availabilityDto)
    {
        _logger.LogDebug("PATCH Request to /couriers/{CourierId}/availability with data: {@AvailabilityDto}",
            courierId, availabilityDto);
        if (courierId == Guid.Empty)
        {
            return BadRequest("Invalid courier ID.");
        }

        var success = await _service.UpdateCourierAvailabilityAsync(courierId, availabilityDto.IsAvailable);
        _logger.LogDebug("Courier availability updated: {Success}", success);

        if (!success)
        {
            return StatusCode(500, "Failed to update courier availability.");
        }

        return Ok();
    }

    [HttpPatch("{courierId:guid}/rating")]
    public async Task<IActionResult> UpdateRating([FromRoute] Guid courierId,
        [FromBody] CourierRatingUpdateDto ratingDto)
    {
        _logger.LogDebug("PATCH Request to /couriers/{CourierId}/rating with data: {@RatingDto}",
            courierId, ratingDto);
        if (courierId == Guid.Empty)
        {
            return BadRequest("Invalid courier ID.");
        }

        var success = await _service.UpdateCourierRatingAsync(courierId, ratingDto.Rating);
        _logger.LogDebug("Courier rating updated: {Success}", success);

        if (!success)
        {
            return StatusCode(500, "Failed to update courier rating.");
        }

        return Ok();
    }

    [HttpPatch("{courierId:guid}/location")]
    public async Task<IActionResult> UpdateLocation([FromRoute] Guid courierId,
        [FromBody] CourierLocationUpdateDto locationDto)
    {
        _logger.LogDebug("PATCH Request to /couriers/{CourierId}/location with data: {@LocationDto}",
            courierId, locationDto);
        if (courierId == Guid.Empty)
        {
            return BadRequest("Invalid courier ID.");
        }

        var success = await _service.UpdateCourierLocationAsync(courierId, locationDto.Latitude, locationDto.Longitude);
        _logger.LogDebug("Courier location updated: {Success}", success);

        if (!success)
        {
            return StatusCode(500, "Failed to update courier location.");
        }

        return Ok();
    }
}