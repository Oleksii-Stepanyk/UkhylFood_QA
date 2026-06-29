using KSE.DistributedSystems.CustomerService.DataAccess.Models;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using AutoMapper;
using KSE.DistributedSystems.OrderService.Models;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace KSE.DistributedSystems.CustomerService.Controllers;

[ApiController]
[Route("orders")]
public class OrderController : ControllerBase
{
    private readonly IPublishEndpoint _endpoint;
    private readonly IMapper _mapper;
    private readonly ILogger<OrderController> _logger;

    public OrderController(IPublishEndpoint publishEndpoint, IMapper mapper, ILogger<OrderController> logger)
    {
        _endpoint = publishEndpoint;
        _mapper = mapper;
        _logger = logger;
    }
    
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] OrderCreateDTO orderCreateDto)
    {
        _logger.LogInformation("Handling POST request");
        
        var order = _mapper.Map<Order>(orderCreateDto);
        order.Id = Guid.NewGuid();
        order.Status = OrderStatus.Pending;
        order.PaymentStatus = PaymentStatus.Pending;
        order.CreatedAt = DateTime.UtcNow;
        
        await _endpoint.Publish(order);
        _logger.LogInformation("POST succeeded");
        return Accepted(new { order.Id });
    }
}