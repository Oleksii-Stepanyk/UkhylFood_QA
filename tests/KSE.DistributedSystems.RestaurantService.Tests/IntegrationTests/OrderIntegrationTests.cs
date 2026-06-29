using System.Net;
using System.Net.Http.Json;
using KSE.DistributedSystems.RestaurantService.API.DTOs;
using KSE.DistributedSystems.RestaurantService.DataAccess.Models;
using KSE.DistributedSystems.RestaurantService.DataAccess.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace KSE.DistributedSystems.RestaurantService.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public class OrderIntegrationTests(CustomWebApplicationFactory factory) : BaseApiTests(factory)
{
    
    [Fact]
    public async Task GetAllOrders_ShouldReturnEmptyListInitially()
    {
        using var scope = Factory.Services.CreateScope();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var mockOrderRepository = Mock.Get(orderRepository);
        
        mockOrderRepository.Setup(r =>
                r.GetOrdersByRestaurantId(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync((new List<Order>(), 0));
        
        var restaurantId = Guid.NewGuid();
        var response = await Client.GetAsync($"/api/orders?restaurantId={restaurantId}");

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadFromJsonAsync<PaginatedResult<OrderResponse>>();

        Assert.NotNull(content);
        Assert.Empty(content.Items);
    }
    
    [Fact]
    public async Task UpdateOrderStatus_ShouldReturnNoContent_WhenValueAllowed()
    {
        using var scope = Factory.Services.CreateScope();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            TotalPrice = 25.99f,
            Items = [
                new()
                {
                    Id = Guid.NewGuid(),
                    Name = "Shashlyk",
                    Quantity = 5,
                    Price = 19.91f
                }
            ]
        };
        
        var mockOrderRepository = Mock.Get(orderRepository);
        
        mockOrderRepository
            .Setup(r => r.GetOrderByIdAsync(order.Id))
            .ReturnsAsync(order);

        mockOrderRepository
            .Setup(r => r.UpdateOrderAsync(order.Id, OrderStatus.Confirmed))
            .ReturnsAsync(() =>
            {
                order.Status = OrderStatus.Confirmed;
                return order;
            });
        
        var request = new UpdateOrderStatusRequest
        {
            OrderId = order.Id,
            OrderStatus = OrderStatus.Confirmed
        };
        
        var response = await Client.PatchAsJsonAsync($"/api/orders/{order.Id}", request);

        response.EnsureSuccessStatusCode();

        var updatedOrder = await orderRepository.GetOrderByIdAsync(order.Id);
        
        Assert.Equal(OrderStatus.Confirmed, updatedOrder!.Status);
    }
    
    [Fact]
    public async Task UpdateOrderStatus_ShouldReturnBadRequest_WhenValueNotAllowed()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            RestaurantId = Guid.NewGuid(),
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            TotalPrice = 25.99f,
            Items = [
                new()
                {
                    Id = Guid.NewGuid(),
                    Name = "Shashlyk",
                    Quantity = 5,
                    Price = 19.91f
                }
            ]
        };
        
        var request = new UpdateOrderStatusRequest
        {
            OrderId = order.Id,
            OrderStatus = OrderStatus.InDelivery
        };
        
        var response = await Client.PatchAsJsonAsync($"/api/orders/{order.Id}", request);
        
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("Provided value InDelivery was not allowed", responseContent);
    }
}
