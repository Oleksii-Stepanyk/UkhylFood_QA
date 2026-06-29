using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using System.Net;

namespace KSE.DistributedSystems.Shared.Resilience;

public class ResiliencePolicies
{
    private readonly ResilienceOptions _options;
    private readonly ILogger<ResiliencePolicies> _logger;

    public ResiliencePolicies(IOptions<ResilienceOptions> options, ILogger<ResiliencePolicies> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public ResiliencePipeline CreateHttpClientPipeline(string serviceName)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
                MaxRetryAttempts = _options.Retry.MaxRetryAttempts,
                Delay = _options.Retry.BaseDelay,
                UseJitter = _options.Retry.UseJitter,
                MaxDelay = _options.Retry.MaxDelay,
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    _logger.LogWarning("HTTP retry attempt {AttemptNumber} for {ServiceName}. Exception: {Exception}",
                        args.AttemptNumber, serviceName, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>(),
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(_options.CircuitBreaker.SamplingDuration),
                MinimumThroughput = _options.CircuitBreaker.MinimumThroughput,
                BreakDuration = _options.CircuitBreaker.DurationOfBreak,
                OnOpened = args =>
                {
                    _logger.LogError("Circuit breaker OPENED for {ServiceName}. Duration: {Duration}",
                        serviceName, _options.CircuitBreaker.DurationOfBreak);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("Circuit breaker CLOSED for {ServiceName}", serviceName);
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    _logger.LogWarning("Circuit breaker HALF-OPENED for {ServiceName}", serviceName);
                    return ValueTask.CompletedTask;
                }
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = _options.Timeout.ExternalServiceTimeout,
                OnTimeout = args =>
                {
                    _logger.LogError("Timeout exceeded for {ServiceName} after {Timeout}ms",
                        serviceName, _options.Timeout.ExternalServiceTimeout.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public ResiliencePipeline CreateDatabasePipeline(string contextName)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<InvalidOperationException>()
                    .Handle<TimeoutException>()
                    .Handle<Microsoft.EntityFrameworkCore.DbUpdateException>(),
                MaxRetryAttempts = 2, // Fewer retries for database operations
                Delay = TimeSpan.FromMilliseconds(500),
                UseJitter = true,
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    _logger.LogWarning("Database retry attempt {AttemptNumber} for {ContextName}. Exception: {Exception}",
                        args.AttemptNumber, contextName, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<InvalidOperationException>()
                    .Handle<TimeoutException>()
                    .Handle<Microsoft.EntityFrameworkCore.DbUpdateException>(),
                FailureRatio = 0.7,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(15),
                OnOpened = args =>
                {
                    _logger.LogCritical("Database circuit breaker OPENED for {ContextName}", contextName);
                    return ValueTask.CompletedTask;
                }
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = _options.Timeout.DatabaseTimeout,
                OnTimeout = args =>
                {
                    _logger.LogError("Database timeout for {ContextName} after {Timeout}ms",
                        contextName, _options.Timeout.DatabaseTimeout.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public ResiliencePipeline CreateMessageQueuePipeline(string queueName)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<InvalidOperationException>()
                    .Handle<TimeoutException>()
                    .Handle<Exception>(ex => ex.Message.Contains("RabbitMQ") || ex.Message.Contains("MassTransit")),
                MaxRetryAttempts = _options.Retry.MaxRetryAttempts,
                Delay = _options.Retry.BaseDelay,
                UseJitter = _options.Retry.UseJitter,
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    _logger.LogWarning("Message queue retry attempt {AttemptNumber} for {QueueName}. Exception: {Exception}",
                        args.AttemptNumber, queueName, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = _options.Timeout.MessageQueueTimeout,
                OnTimeout = args =>
                {
                    _logger.LogError("Message queue timeout for {QueueName} after {Timeout}ms",
                        queueName, _options.Timeout.MessageQueueTimeout.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public ResiliencePipeline CreateGenericPipeline(string operationName)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                MaxRetryAttempts = _options.Retry.MaxRetryAttempts,
                Delay = _options.Retry.BaseDelay,
                UseJitter = _options.Retry.UseJitter,
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    _logger.LogWarning("Generic retry attempt {AttemptNumber} for {OperationName}. Exception: {Exception}",
                        args.AttemptNumber, operationName, args.Outcome.Exception?.Message);
                    return ValueTask.CompletedTask;
                }
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = _options.Timeout.DefaultTimeout,
                OnTimeout = args =>
                {
                    _logger.LogError("Generic timeout for {OperationName} after {Timeout}ms",
                        operationName, _options.Timeout.DefaultTimeout.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }
} 