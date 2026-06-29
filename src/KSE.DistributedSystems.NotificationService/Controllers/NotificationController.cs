using KSE.DistributedSystems.NotificationService.Services;
using Microsoft.AspNetCore.Mvc;

namespace KSE.DistributedSystems.NotificationService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly IEmailService _emailSender;
    public NotificationController(IEmailService emailSender)
    {
        _emailSender = emailSender;
    }

    [HttpPost("test-email")]
    public async Task<IActionResult> SendEmail([FromBody] string to)
    {
        await _emailSender.SendEmailAsync(to, "Test email", "Test body");
        return Ok("Email sent");
    }
}