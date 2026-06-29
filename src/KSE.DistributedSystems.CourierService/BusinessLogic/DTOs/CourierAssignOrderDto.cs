using System;
using System.ComponentModel.DataAnnotations;

namespace KSE.DistributedSystems.CourierService.BusinessLogic.DTOs;

public class CourierAssignOrderDto
{
    [Required(ErrorMessage = "Order ID is required.")]
    public required Guid OrderId { get; set; }
}