namespace KSE.DistributedSystems.NotificationService.Services;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body);
}