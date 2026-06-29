using KSE.DistributedSystems.CustomerService.DataAccess.Models;
using KSE.DistributedSystems.CustomerService.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace KSE.DistributedSystems.CustomerService.Controllers;

[ApiController]
[Route("customers")]
public class CustomerController : ControllerBase
{
    private readonly ICustomerService _customerService;
    private readonly ILogger<CustomerController> _logger;

    public CustomerController(ICustomerService customerService, ILogger<CustomerController> logger)
    {
        _customerService = customerService;
        _logger = logger;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<CustomerDTO>> GetCustomer(Guid id)
    {
        _logger.LogInformation("Handling GET request");
        _logger.LogDebug("Retrieving customer with id {Id}", id);
        
        var customer = await _customerService.GetCustomerAsync(id);
        if (customer == null)
        {
            _logger.LogInformation("Customer with id {Id} was not found", id);
            return NotFound();
        }
        
        _logger.LogInformation("GET succeeded");
        return Ok(customer);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCustomer(Guid id, [FromBody] CustomerDTO customer)
    {
        _logger.LogInformation("Handling PUT request");
        _logger.LogDebug("Route id {Id}. Customer {@Customer}", id, customer);

        if (id != customer.Id)
        {
            _logger.LogWarning("Ids mismatch on request. Route id {RouteId}, Customer id {CustomerId}", id, customer.Id);
            return BadRequest();
        }
        await _customerService.UpdateCustomerAsync(id, customer);
        _logger.LogInformation("PUT succeeded");
        return NoContent();
    }
}
