using Microsoft.AspNetCore.Mvc;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Services;
using FluentValidation;
using System.ComponentModel.DataAnnotations;
using Asp.Versioning;

namespace KSE.DistributedSystems.PaymentService.API.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IValidator<PaymentRequestDto> _paymentValidator;
    private readonly ILogger<PaymentsController> _logger;
    private readonly PaymentMonitoringService _monitoringService;

    public PaymentsController(
        IPaymentService paymentService,
        IValidator<PaymentRequestDto> paymentValidator,
        ILogger<PaymentsController> logger,
        PaymentMonitoringService monitoringService)
    {
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _paymentValidator = paymentValidator ?? throw new ArgumentNullException(nameof(paymentValidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _monitoringService = monitoringService ?? throw new ArgumentNullException(nameof(monitoringService));
    }
    
    [HttpPost]
    [ProducesResponseType(typeof(PaymentResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ProcessPayment([FromBody] PaymentRequestDto request)
    {
        if (request == null)
            return BadRequest("Payment request is required");
        
        var validationResult = await _paymentValidator.ValidateAsync(request);
        if (!validationResult.IsValid)
        {
            var problemDetails = new ValidationProblemDetails();
            foreach (var error in validationResult.Errors)
            {
                problemDetails.Errors.TryAdd(error.PropertyName, new[] { error.ErrorMessage });
            }
            return BadRequest(problemDetails);
        }

        try
        {
            var result = await _paymentService.ProcessPaymentAsync(request);
            return CreatedAtAction(nameof(GetPayment), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment for order {OrderId}", request.OrderId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                new { message = "An error occurred while processing the payment" });
        }
    }
    
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PaymentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPayment([FromRoute] Guid id)
    {
        if (id == Guid.Empty)
            return BadRequest("Invalid payment ID");

        var payment = await _paymentService.GetPaymentAsync(id);
        if (payment == null)
            return NotFound($"Payment with ID {id} not found");

        return Ok(payment);
    }
    
    [HttpGet("order/{orderId:guid}")]
    [ProducesResponseType(typeof(PaymentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPaymentByOrderId([FromRoute] Guid orderId)
    {
        if (orderId == Guid.Empty)
            return BadRequest("Invalid order ID");

        var payment = await _paymentService.GetPaymentByOrderIdAsync(orderId);
        if (payment == null)
            return NotFound($"Payment for order {orderId} not found");

        return Ok(payment);
    }
    
    [HttpGet("customer/{customerId:guid}")]
    [ProducesResponseType(typeof(IEnumerable<PaymentResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCustomerPayments([FromRoute] Guid customerId)
    {
        if (customerId == Guid.Empty)
            return BadRequest("Invalid customer ID");

        var payments = await _paymentService.GetCustomerPaymentsAsync(customerId);
        return Ok(payments);
    }
    
    [HttpGet("customer/{customerId:guid}/history")]
    [ProducesResponseType(typeof(IEnumerable<PaymentResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPaymentHistory(
        [FromRoute] Guid customerId,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        if (customerId == Guid.Empty)
            return BadRequest("Invalid customer ID");

        if (from.HasValue && to.HasValue && from > to)
            return BadRequest("Start date cannot be after end date");

        var payments = await _paymentService.GetPaymentHistoryAsync(customerId, from, to);
        return Ok(payments);
    }
    
    [HttpPost("refund")]
    [ProducesResponseType(typeof(PaymentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RefundPayment([FromBody] RefundRequestDto request)
    {
        if (request == null)
            return BadRequest("Refund request is required");

        if (request.PaymentId == Guid.Empty)
            return BadRequest("Invalid payment ID");

        try
        {
            var result = await _paymentService.RefundPaymentAsync(request);
            if (result == null)
                return NotFound($"Payment with ID {request.PaymentId} not found");

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing refund for payment {PaymentId}", request.PaymentId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while processing the refund" });
        }
    }
    
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(PaymentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CancelPayment([FromRoute] Guid id)
    {
        if (id == Guid.Empty)
            return BadRequest("Invalid payment ID");

        try
        {
            var result = await _paymentService.CancelPaymentAsync(id);
            if (result == null)
                return NotFound($"Payment with ID {id} not found");

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling payment {PaymentId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { message = "An error occurred while cancelling the payment" });
        }
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePaymentStatus(
        [FromRoute] Guid id,
        [FromBody] PaymentStatusUpdateRequest request)
    {
        if (id == Guid.Empty)
            return BadRequest("Invalid payment ID");

        if (request == null)
            return BadRequest("Status update request is required");

        var success = await _paymentService.UpdatePaymentStatusAsync(id, request.Status, request.Reason);
        if (!success)
            return NotFound($"Payment with ID {id} not found");

        return Ok(new { message = "Payment status updated successfully" });
    }

    [HttpGet("metrics/test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult TestPaymentMetrics()
    {
        var random = new Random();
        var paymentMethods = new[] { "CreditCard", "DebitCard", "PayPal", "ApplePay" };
        
        for (int i = 0; i < 5; i++)
        {
            var method = paymentMethods[random.Next(paymentMethods.Length)];
            var isSuccess = random.NextDouble() > 0.3;
            var amount = (decimal)(random.NextDouble() * 1000 + 10);
            
            if (isSuccess)
            {
                _monitoringService.IncrementPaymentStatus("success", method, amount);
                _monitoringService.IncrementPaymentProcessed("test_payment", "success", method);
            }
            else
            {
                _monitoringService.IncrementPaymentStatus("failed", method, amount);
                _monitoringService.IncrementPaymentProcessed("test_payment", "failed", method);
            }
            
            _monitoringService.RecordPaymentProcessingDuration("test_payment", 
                random.NextDouble() * 2000 + 100, isSuccess, method);
        }

        return Ok(new { 
            message = "Test payment metrics recorded successfully",
            timestamp = DateTime.UtcNow,
            metricsGenerated = 5
        });
    }
}

public class PaymentStatusUpdateRequest
{
    [Required]
    public PaymentStatus Status { get; set; }
    
    public string? Reason { get; set; }
} 