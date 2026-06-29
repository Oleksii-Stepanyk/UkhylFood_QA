using System.ComponentModel.DataAnnotations;

namespace KSE.DistributedSystems.CourierService.BusinessLogic.DTOs;

public class CourierAvailabilityUpdateDto
{
    [Required(ErrorMessage = "Availability status is required.")]
    public bool IsAvailable { get; set; }
}