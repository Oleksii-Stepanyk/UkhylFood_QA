using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace KSE.DistributedSystems.RestaurantService.API.Middlewares;

public class LogMiddleware : IMiddleware
{
    private readonly Guid _id = Guid.NewGuid();
    private readonly ILogger<LogMiddleware> _logger;

    public LogMiddleware(ILogger<LogMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var currentTimestamp = DateTime.UtcNow.ToString("o");

        _logger.LogInformation("[{MiddlewareId}] Incoming request at {Timestamp}: {Method} {Path} from {IP}",
            _id,
            currentTimestamp,
            context.Request.Method,
            context.Request.Path,
            context.Connection.RemoteIpAddress);

        try
        {
            await next(context);
        }
        catch (Exception e)
        {
            _logger.LogError(e,
                "[{MiddlewareId}] Error processing request at {Timestamp}: {Method} {Path}. Exception: {ExceptionMessage}",
                _id,
                DateTime.UtcNow.ToString("o"),
                context.Request.Method,
                context.Request.Path,
                e.Message);
        }
        finally
        {
            _logger.LogInformation("[{MiddlewareId}] Outgoing response at {Timestamp}: {StatusCode}",
                _id,
                DateTime.UtcNow.ToString("o"),
                context.Response.StatusCode);
        }
    }
}