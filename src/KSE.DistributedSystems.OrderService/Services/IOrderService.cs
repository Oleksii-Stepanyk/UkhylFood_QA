using KSE.DistributedSystems.OrderService.Models;

namespace KSE.DistributedSystems.OrderService.Services.Interfaces;

public interface IOrderService
{
    Task OnOrderPlaced(Order order, MassTransit.IPublishEndpoint publishEndpoint);
    Task OnPaymentSuccess(PaymentResult result, MassTransit.IPublishEndpoint publishEndpoint);
    Task OnPaymentFail(PaymentResult result, MassTransit.IPublishEndpoint publishEndpoint);
    Task OnOrderUpdate(Order updateOrder, MassTransit.IPublishEndpoint publishEndpoint);
}