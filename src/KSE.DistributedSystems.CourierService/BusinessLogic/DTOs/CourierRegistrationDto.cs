using System.ComponentModel.DataAnnotations;
using KSE.DistributedSystems.CourierService.BusinessLogic.Attributes;
using KSE.DistributedSystems.CourierService.DataAccess.Entities;

namespace KSE.DistributedSystems.CourierService.BusinessLogic.DTOs;

public class CourierRegistrationDto
{
    [Required(ErrorMessage = "First name is required.")]
    [MinLength(2, ErrorMessage = "First name must be at least 2 characters long.")]
    [StringLength(32, ErrorMessage = "First name cannot exceed 32 characters.")]
    public required string FirstName { get; set; }

    [Required(ErrorMessage = "Last name is required.")]
    [MinLength(3, ErrorMessage = "Last name must be at least 3 characters long.")]
    [StringLength(64, ErrorMessage = "Last name cannot exceed 64 characters.")]
    public required string LastName { get; set; }

    [Required(ErrorMessage = "Vehicle type is required.")]
    [EnumValidation(typeof(VehicleType), ErrorMessage = "Invalid vehicle type.")]
    public required string VehicleType { get; set; }
}