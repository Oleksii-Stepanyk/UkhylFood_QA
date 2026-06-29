using AutoMapper;
using KSE.DistributedSystems.CustomerService.DataAccess.Entities;

namespace KSE.DistributedSystems.CustomerService.DataAccess.Models;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Customer, CustomerDTO>();
        CreateMap<CustomerDTO, Customer>()
            .ForMember(dest => dest.PaymentMethods, opt => opt.Ignore());
        CreateMap<PaymentMethod, PaymentMethodDTO>()
            .ForMember(dest => dest.Type, opt => opt.MapFrom(src => src.Type.ToString()));
        CreateMap<PaymentMethodDTO, PaymentMethod>()
            .ForMember(dest => dest.Type, opt => opt.Ignore())
            .ForMember(dest => dest.Details, opt => opt.Ignore())
            .ForMember(dest => dest.Customer, opt => opt.Ignore());

        // Map OrderCreateDTO to Order (OrderService model)
        CreateMap<OrderCreateDTO, KSE.DistributedSystems.OrderService.Models.Order>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Status, opt => opt.Ignore())
            .ForMember(dest => dest.PaymentStatus, opt => opt.Ignore())
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.DeliveredAt, opt => opt.Ignore())
            .ForMember(dest => dest.CourierId, opt => opt.Ignore());
        CreateMap<OrderItemDTO, KSE.DistributedSystems.OrderService.Models.OrderItem>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Orders, opt => opt.Ignore());
    }
}