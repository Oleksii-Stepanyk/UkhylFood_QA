using System;
using AutoMapper;
using KSE.DistributedSystems.CourierService.BusinessLogic.DTOs;
using KSE.DistributedSystems.CourierService.DataAccess.Entities;

namespace KSE.DistributedSystems.CourierService.BusinessLogic.MappingProfiles;

public class CourierProfile : Profile
{
    public CourierProfile()
    {
        CreateMap<Courier, CourierReadDto>()
            .ForMember(dest => dest.FullName, opt => opt.MapFrom(src => $"{src.FirstName} {src.LastName}"))
            .ForMember(dest => dest.Rating, opt => opt.MapFrom(src => Math.Round(src.Rating, 2)))
            .ForMember(dest => dest.VehicleType, opt => opt.MapFrom(src => src.VehicleType.ToString()));

        CreateMap<CourierRegistrationDto, Courier>()
            .ForMember(dest => dest.Id, opt => opt.MapFrom(_ => Guid.NewGuid()))
            .ForMember(dest => dest.VehicleType, opt => opt.MapFrom(src => Enum.Parse<VehicleType>(src.VehicleType, true)));
    }
}