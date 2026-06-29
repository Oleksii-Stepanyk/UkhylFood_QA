using AutoMapper;
using KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;

namespace KSE.DistributedSystems.PaymentService.BusinessLogic.MappingProfiles;

public class PaymentProfile : Profile
{
    public PaymentProfile()
    {
        CreateMap<PaymentRequestDto, Payment>()
            .ForMember(dest => dest.Id, opt => opt.Ignore())
            .ForMember(dest => dest.Status, opt => opt.MapFrom(src => PaymentStatus.Pending))
            .ForMember(dest => dest.CreatedAt, opt => opt.Ignore())
            .ForMember(dest => dest.ProcessedAt, opt => opt.Ignore())
            .ForMember(dest => dest.RefundedAt, opt => opt.Ignore())
            .ForMember(dest => dest.ExternalPaymentId, opt => opt.Ignore())
            .ForMember(dest => dest.FailureReason, opt => opt.Ignore())
            .ForMember(dest => dest.Events, opt => opt.Ignore())
            .ForMember(dest => dest.Metadata, opt => opt.MapFrom(src => MapMetadata(src)));

        CreateMap<Payment, PaymentResponseDto>()
            .ForMember(dest => dest.Metadata, opt => opt.MapFrom(src => MapMetadataDto(src.Metadata)));

        CreateMap<PaymentMetadata, PaymentMetadataDto>();
        CreateMap<PaymentMetadataDto, PaymentMetadata>();
    }

    private static PaymentMetadata MapMetadata(PaymentRequestDto src)
    {
        var metadata = new PaymentMetadata
        {
            AdditionalData = src.AdditionalData
        };

        if (src.CardDetails != null)
        {
            metadata.CardLast4 = src.CardDetails.CardNumber.Length >= 4 
                ? src.CardDetails.CardNumber[^4..] 
                : string.Empty;
            metadata.CardBrand = DetectCardBrand(src.CardDetails.CardNumber);
        }

        return metadata;
    }

    private static PaymentMetadataDto MapMetadataDto(PaymentMetadata metadata)
    {
        return new PaymentMetadataDto
        {
            CardLast4 = metadata.CardLast4,
            CardBrand = metadata.CardBrand,
            AdditionalData = metadata.AdditionalData
        };
    }

    private static string DetectCardBrand(string cardNumber)
    {
        var cleanNumber = cardNumber.Replace(" ", "").Replace("-", "");
        
        if (cleanNumber.StartsWith("4"))
            return "Visa";
        if (cleanNumber.StartsWith("5") || cleanNumber.StartsWith("2"))
            return "Mastercard";
        if (cleanNumber.StartsWith("3"))
            return "American Express";
        
        return "Unknown";
    }
} 