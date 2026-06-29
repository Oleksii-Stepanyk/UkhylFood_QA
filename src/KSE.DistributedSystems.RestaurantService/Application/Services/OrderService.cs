using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using KSE.DistributedSystems.RestaurantService.API.DTOs;
using KSE.DistributedSystems.RestaurantService.Application.Exceptions;
using KSE.DistributedSystems.RestaurantService.DataAccess.Models;
using KSE.DistributedSystems.RestaurantService.DataAccess.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace KSE.DistributedSystems.RestaurantService.Application.Services;

public class OrderService : IOrderService
{
    private readonly IMapper _mapper;
    private readonly IPublishEndpoint _endpoint;
    private readonly IOrderRepository _repository;
    private readonly OrderMonitoringService _monitoring;
    private readonly ILogger<OrderService> _logger;

    public OrderService(IPublishEndpoint endpoint, IOrderRepository repository, IMapper mapper, ILogger<OrderService> logger, OrderMonitoringService monitoring)
    {
        _endpoint = endpoint;
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
        _monitoring = monitoring;
    }

    public async Task<OrderResponse> GetOrderByIdAsync(Guid id)
    {
        _logger.LogInformation("Retrieving order with id {Id}", id);
        var order = await _repository.GetOrderByIdAsync(id);
        if (order == null)
        {
            _logger.LogWarning("Order with id {Id} was not found", id);
            throw new OrderNotFoundException(id);
        }
            
        _logger.LogInformation("Retrieving order with id {Id} successful", id);
        return _mapper.Map<Order, OrderResponse>(order);
    }

    public async Task<OrderResponse> UpdateOrderStatusAsync(UpdateOrderStatusRequest request)
    {
        if (request.OrderStatus is OrderStatus.Cancelled or OrderStatus.Delivered or OrderStatus.InDelivery)
        {
            _logger.LogWarning("Unallowed status for order received {Status}", request.OrderStatus);
            throw new UnallowedValueException(request.OrderStatus.ToString("F"));
        }
            
        _logger.LogInformation("Updating order with id {Id}", request.OrderId);
        
        var order = await _repository.GetOrderByIdAsync(request.OrderId);
        if (order == null)
        {
            _logger.LogWarning("Order with id {Id} was not found", request.OrderId);
            throw new OrderNotFoundException(request.OrderId);
        }

        order.Status = request.OrderStatus;
        var sharedOrder = _mapper.Map<Order, KSE.DistributedSystems.OrderService.Models.Order>(order);
        await _endpoint.Publish(sharedOrder);
        
        var updated = await _repository.UpdateOrderAsync(request.OrderId, request.OrderStatus);
        
        _logger.LogInformation("Order with id {Id} published", request.OrderId);
        _logger.LogDebug("Published entity {@Entity}", sharedOrder);
        
        return _mapper.Map<Order, OrderResponse>(updated);
    }

    public async Task CreateOrderFromQueueAsync(Order order)
    {
        var stopWatch = new Stopwatch();
        stopWatch.Start();
        var success = false;
        
        try
        {
            await _repository.CreateOrderAsync(order);
            success = true;
        }
        finally
        {
            stopWatch.Stop();
            _monitoring.RecordOrderProcessingDuration("create_order", stopWatch.ElapsedMilliseconds, success);
        }
    }

    public async Task UpdateOrderStatusFromQueueAsync(Order order)
    {
        var stopWatch = new Stopwatch();
        stopWatch.Start();
        var success = false;
        
        try
        {
            await _repository.UpdateOrderAsync(order.Id, order.Status);
            _monitoring.IncrementOrderProcessed("update_order_status", order.Status.ToString("F"));
            success = true;
        }
        finally
        {
            stopWatch.Stop();
            _monitoring.RecordOrderProcessingDuration("update_order_status", stopWatch.ElapsedMilliseconds, success);
        }
    }

    public async Task<PaginatedResult<OrderResponse>> GetOrdersByRestaurantId(Guid restaurantId, int offset, int limit)
    {
        _logger.LogInformation("Retrieving orders by restaurant id {Id}", restaurantId);
        var (orders, totalCount) = await _repository.GetOrdersByRestaurantId(restaurantId, offset, limit);

        var ordersDto = orders.Select(o => _mapper.Map<Order, OrderResponse>(o)).ToList();
        _logger.LogInformation("Retrieval successful");
        return new PaginatedResult<OrderResponse>(ordersDto, offset, limit, totalCount);
    }
}