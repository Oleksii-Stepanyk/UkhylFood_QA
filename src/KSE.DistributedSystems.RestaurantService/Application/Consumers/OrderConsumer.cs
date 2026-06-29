using System.Threading.Tasks;
using KSE.DistributedSystems.RestaurantService.Application.Services;
using LocalOrder = KSE.DistributedSystems.RestaurantService.DataAccess.Models.Order;
using KSE.DistributedSystems.RestaurantService.DataAccess.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;
using AutoMapper;
using OrderFromQueue = KSE.DistributedSystems.OrderService.Models.Order;
using LocalOrderStatus = KSE.DistributedSystems.RestaurantService.DataAccess.Models.OrderStatus;

using LogContext = Serilog.Context.LogContext;

namespace KSE.DistributedSystems.RestaurantService.Application.Consumers;

public class OrderConsumer : IConsumer<OrderFromQueue>
{
    private readonly IOrderService _orderService;
    private readonly IOrderRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<OrderConsumer> _logger;

    public OrderConsumer(ILogger<OrderConsumer> logger, IOrderService orderService, IOrderRepository repository, IMapper mapper)
    {
        _logger = logger;
        _orderService = orderService;
        _repository = repository;
        _mapper = mapper;
    }

    public async Task Consume(ConsumeContext<OrderFromQueue> context)
    {
        using var _ = LogContext.PushProperty("MessageId", context.MessageId);
        var orderMsg = context.Message;
        _logger.LogInformation("Received order with id {OrderId} and status {Status}", orderMsg.Id, orderMsg.Status);
        
        var order = _mapper.Map<OrderFromQueue, LocalOrder>(orderMsg);
        
        switch (order.Status)
        {
            case LocalOrderStatus.Pending:
                var existing = await _repository.GetOrderByIdAsync(order.Id);
                if (existing == null)
                {
                    _logger.LogInformation("Creating pending order {OrderId} in restaurant database", order.Id);
                    await _orderService.CreateOrderFromQueueAsync(order);
                }
                else
                {
                    _logger.LogInformation("Order {OrderId} already exists in restaurant database, skipping creation.", order.Id);
                }
                break;
            case LocalOrderStatus.Delivered:
            case LocalOrderStatus.Cancelled:
            case LocalOrderStatus.InDelivery:
                _logger.LogInformation("Processing status {Status}", order.Status);
                await _orderService.UpdateOrderStatusFromQueueAsync(order);
                break;
            case LocalOrderStatus.Confirmed:
            case LocalOrderStatus.Preparing:
            case LocalOrderStatus.ReadyForPickup:
            default:
                _logger.LogInformation("Order with ID {OrderId} has changed status to {Status}", order.Id,
                    order.Status);
                break;
        }
    }
}