using System.ComponentModel.DataAnnotations;

namespace KSE.DistributedSystems.CourierService.BusinessLogic.DTOs;

public class CourierRatingUpdateDto
{
    [Required(ErrorMessage = "Rating is required.")]
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5.")]
    public required float Rating { get; set; }
}