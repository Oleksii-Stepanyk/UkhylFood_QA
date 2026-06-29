using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using KSE.DistributedSystems.CourierService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.OrderService.Models;
using MassTransit;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;

namespace KSE.DistributedSystems.CourierService.BusinessLogic.Consumers;

public class OrderConsumer : IConsumer<Order>
{
    private readonly ICourierService _courierService;
    private readonly ILogger<OrderConsumer> _logger;
    private readonly Tracer _tracer;

    public OrderConsumer(ICourierService courierService, ILogger<OrderConsumer> logger, Tracer tracer)
    {
        _courierService = courierService ?? throw new ArgumentNullException(nameof(courierService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    }

    public async Task Consume(ConsumeContext<Order> context)
    {
        var order = context.Message;

        if (order == null)
        {
            _logger.LogError("Received null order message");
            throw new ArgumentNullException(nameof(order));
        }

        if (order.CourierId != Guid.Empty)
        {
            _logger.LogDebug("Order {OrderId} already has a courier assigned", order.Id);
            return;
        }

        switch (order.Status)
        {
            // This is in case if we fail to assign a courier after the confirmation
            case OrderStatus.Preparing:
            case OrderStatus.ReadyForPickup:
                if (order.CourierId.HasValue)
                {
                    _logger.LogInformation(
                        "Order {OrderId} already has a courier assigned, skipping assignment",
                        order.Id);
                    return;
                }

                _logger.LogInformation("Order {OrderId} is in delivery", order.Id);
                _logger.LogInformation("Assigning courier to order {OrderId}", order.Id);

                var startTime = DateTime.UtcNow;
                try
                {
                    var courierId = await _courierService.AssignOrderToCourierAsync(order);
                    _logger.LogInformation("Assigned courier {CourierId} to order {OrderId}", courierId, order.Id);
                    Metrics.OrderAssignmentAttempts.Add(1, new KeyValuePair<string, object?>("status", "success"));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to assign courier to order {OrderId}", order.Id);
                    Metrics.OrderAssignmentAttempts.Add(1, new KeyValuePair<string, object?>("status", "failed"));
                    throw;
                }
                finally
                {
                    var endTime = DateTime.UtcNow;
                    var duration = (endTime - startTime).TotalMilliseconds;

                    Metrics.OrderAssignmentDuration.Record(duration);
                }

                break;

            case OrderStatus.Delivered:
            case OrderStatus.Cancelled:
                if (order.CourierId.HasValue)
                {
                    _logger.LogInformation(
                        "Marking courier {CourierId} as available after order {OrderId} status change to {Status}",
                        order.CourierId, order.Id, order.Status.ToString());
                    try
                    {
                        await _courierService.UpdateCourierAvailabilityAsync((Guid)order.CourierId, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to mark courier {CourierId} as available", order.CourierId);
                        throw;
                    }
                }

                break;

            case OrderStatus.Pending:
            case OrderStatus.Confirmed:
            case OrderStatus.InDelivery:
            default:
                _logger.LogInformation("Order with ID {OrderId} has changed status to {Status}", order.Id,
                    order.Status);
                break;
        }
    }
}