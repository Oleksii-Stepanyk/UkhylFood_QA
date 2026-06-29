namespace KSE.DistributedSystems.Shared.Resilience;

public class ResilienceOptions
{
    public const string SectionName = "Resilience";

    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();
    public RetryOptions Retry { get; set; } = new();
    public TimeoutOptions Timeout { get; set; } = new();
    public BulkheadOptions Bulkhead { get; set; } = new();
}

public class CircuitBreakerOptions
{
    public int HandledEventsAllowedBeforeBreaking { get; set; } = 5;
    public TimeSpan DurationOfBreak { get; set; } = TimeSpan.FromSeconds(30);
    public int SamplingDuration { get; set; } = 60;
    public int MinimumThroughput { get; set; } = 10;
}

public class RetryOptions
{
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
    public bool UseJitter { get; set; } = true;
}

public class TimeoutOptions
{
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan DatabaseTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ExternalServiceTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan MessageQueueTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

public class BulkheadOptions
{
    public int MaxParallelization { get; set; } = 10;
    public int MaxQueuedActions { get; set; } = 25;
} 