using System;
using AutoMapper;
using KSE.DistributedSystems.RestaurantService.API.DTOs;
using KSE.DistributedSystems.RestaurantService.DataAccess.Models;
using MassTransit.Logging;

namespace KSE.DistributedSystems.RestaurantService.Application.MappingProfiles;

public class OrderProfile : Profile
{
    public OrderProfile()
    {
        CreateMap<Order, OrderResponse>();
        CreateMap<KSE.DistributedSystems.OrderService.Models.Order, Order>().ReverseMap();
        CreateMap<KSE.DistributedSystems.OrderService.Models.OrderItem, OrderItem>().ReverseMap();
    }
}