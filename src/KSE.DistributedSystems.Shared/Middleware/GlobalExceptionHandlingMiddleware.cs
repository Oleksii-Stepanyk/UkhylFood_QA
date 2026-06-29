using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using Polly.Timeout;

namespace KSE.DistributedSystems.Shared.Middleware;

public class GlobalExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;

    public GlobalExceptionHandlingMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next.Invoke(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.TraceIdentifier;
        
        _logger.LogError(exception, "An unhandled exception occurred. CorrelationId: {CorrelationId}", correlationId);

        var (statusCode, message) = GetErrorResponse(exception);

        var response = new ErrorResponse
        {
            CorrelationId = correlationId,
            Message = message,
            Type = exception.GetType().Name,
            Timestamp = DateTime.UtcNow
        };

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(jsonResponse);
    }

    private static (HttpStatusCode statusCode, string message) GetErrorResponse(Exception exception)
    {
        return exception switch
        {
            TimeoutRejectedException => (HttpStatusCode.RequestTimeout, 
                "The request timed out. Please try again."),
            ArgumentNullException => (HttpStatusCode.BadRequest, 
                "Required parameters are missing."),
            ArgumentException => (HttpStatusCode.BadRequest, 
                "Invalid request parameters."),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, 
                "You are not authorized to perform this action."),
            KeyNotFoundException => (HttpStatusCode.NotFound, 
                "The requested resource was not found."),
            InvalidOperationException => (HttpStatusCode.Conflict, 
                "The operation cannot be completed in the current state."),
            HttpRequestException => (HttpStatusCode.BadGateway, 
                "Failed to communicate with external service."),
            _ => (HttpStatusCode.InternalServerError, 
                "An internal server error occurred. Please try again later.")
        };
    }
}

public class ErrorResponse
{
    public string CorrelationId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
} 