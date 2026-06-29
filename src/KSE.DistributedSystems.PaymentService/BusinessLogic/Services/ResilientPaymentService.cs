using AutoMapper;
using KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;
using KSE.DistributedSystems.PaymentService.DataAccess.Interfaces;
using KSE.DistributedSystems.Shared.Resilience;
using MassTransit;
using Microsoft.Extensions.Caching.Memory;
using Polly;
using StackExchange.Redis;
using System.Text.Json;
using System.Diagnostics;

namespace KSE.DistributedSystems.PaymentService.BusinessLogic.Services;

public class ResilientPaymentService : IPaymentService
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentProcessor _processor;
    private readonly IMapper _mapper;
    private readonly ILogger<ResilientPaymentService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly IDatabase _redisCache;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly PaymentMonitoringService _monitoringService;
    private readonly ResiliencePipeline _databasePipeline;
    private readonly ResiliencePipeline _messagingPipeline;
    private readonly ResiliencePipeline _cachePipeline;

    public ResilientPaymentService(
        IPaymentRepository repository,
        IPaymentProcessor processor,
        IMapper mapper,
        ILogger<ResilientPaymentService> logger,
        IMemoryCache memoryCache,
        IConnectionMultiplexer redis,
        ISendEndpointProvider sendEndpointProvider,
        PaymentMonitoringService monitoringService,
        ResiliencePolicies resiliencePolicies)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _sendEndpointProvider = sendEndpointProvider ?? throw new ArgumentNullException(nameof(sendEndpointProvider));
        _monitoringService = monitoringService ?? throw new ArgumentNullException(nameof(monitoringService));

        if (!redis.IsConnected)
            throw new InvalidOperationException("Redis cache is not connected.");
        _redisCache = redis.GetDatabase();

        _databasePipeline = resiliencePolicies.CreateDatabasePipeline("PaymentService");
        _messagingPipeline = resiliencePolicies.CreateMessageQueuePipeline("PaymentQueue");
        _cachePipeline = resiliencePolicies.CreateGenericPipeline("RedisCache");
    }

    public async Task<PaymentResponseDto> ProcessPaymentAsync(PaymentRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var success = false;
        var paymentMethod = request.PaymentMethod.ToString();

        _logger.LogInformation("Processing payment for order {OrderId}, customer {CustomerId}, amount {Amount} {Currency}",
            request.OrderId, request.CustomerId, request.Amount, request.Currency);

        try
        {
            var existingPayment = await _databasePipeline.ExecuteAsync(async _ =>
                await _repository.GetByOrderIdAsync(request.OrderId));

            if (existingPayment != null)
            {
                _logger.LogWarning("Payment already exists for order {OrderId}", request.OrderId);
                throw new InvalidOperationException($"Payment already exists for order {request.OrderId}");
            }

            var payment = _mapper.Map<Payment>(request);
            payment.Id = Guid.NewGuid();

            var savedPayment = await _databasePipeline.ExecuteAsync(async _ =>
                await _repository.AddAsync(payment));

            await AddPaymentEventAsync(savedPayment.Id, PaymentEventType.Created, "Payment created");

            savedPayment.Status = PaymentStatus.Processing;
            await _databasePipeline.ExecuteAsync(async _ =>
                await _repository.UpdateAsync(savedPayment));

            await AddPaymentEventAsync(savedPayment.Id, PaymentEventType.Processing, "Payment processing started");

            var processingResult = await _processor.ProcessAsync(savedPayment);

            if (processingResult.IsSuccess)
            {
                savedPayment.Status = PaymentStatus.Succeeded;
                savedPayment.ProcessedAt = DateTime.UtcNow;
                savedPayment.ExternalPaymentId = processingResult.ExternalPaymentId;
                savedPayment.Metadata.ProcessorResponse = JsonSerializer.Serialize(processingResult.ProcessorResponse);

                await AddPaymentEventAsync(savedPayment.Id, PaymentEventType.Succeeded,
                    $"Payment succeeded with external ID: {processingResult.ExternalPaymentId}");

                await _messagingPipeline.ExecuteAsync(async _ =>
                    await PublishPaymentEventAsync("payment.succeeded", savedPayment));

                _monitoringService.IncrementPaymentStatus("success", paymentMethod, savedPayment.Amount);
                _monitoringService.IncrementPaymentProcessed("process_payment", "success", paymentMethod);
                success = true;
            }
            else
            {
                savedPayment.Status = PaymentStatus.Failed;
                savedPayment.FailureReason = processingResult.ErrorMessage;
                savedPayment.Metadata.ProcessorResponse = JsonSerializer.Serialize(processingResult.ProcessorResponse);

                await AddPaymentEventAsync(savedPayment.Id, PaymentEventType.Failed,
                    $"Payment failed: {processingResult.ErrorMessage}");

                await _messagingPipeline.ExecuteAsync(async _ =>
                    await PublishPaymentEventAsync("payment.failed", savedPayment));

                _monitoringService.IncrementPaymentStatus("failed", paymentMethod, savedPayment.Amount);
                _monitoringService.IncrementPaymentProcessed("process_payment", "failed", paymentMethod);
            }

            await _databasePipeline.ExecuteAsync(async _ =>
                await _repository.UpdateAsync(savedPayment));

            await CachePaymentAsync(savedPayment);

            var result = _mapper.Map<PaymentResponseDto>(savedPayment);

            _logger.LogInformation("Payment processing completed for {PaymentId} with status {Status}",
                savedPayment.Id, savedPayment.Status);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment for order {OrderId}", request.OrderId);

            _monitoringService.IncrementPaymentStatus("error", paymentMethod, request.Amount);
            _monitoringService.IncrementPaymentProcessed("process_payment", "error", paymentMethod);

            throw;
        }
        finally
        {
            stopwatch.Stop();
            _monitoringService.RecordPaymentProcessingDuration("process_payment", stopwatch.ElapsedMilliseconds, success, paymentMethod);
        }
    }

    public async Task<PaymentResponseDto?> GetPaymentAsync(Guid paymentId)
    {
        if (paymentId == Guid.Empty)
            throw new ArgumentException("Payment ID cannot be empty", nameof(paymentId));

        try
        {
            var cached = await GetCachedPaymentAsync(paymentId);
            if (cached != null)
                return cached;

            var payment = await _databasePipeline.ExecuteAsync(async _ =>
                await _repository.GetByIdAsync(paymentId));

            if (payment == null)
                return null;

            var result = _mapper.Map<PaymentResponseDto>(payment);

            await CachePaymentAsync(payment);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving payment {PaymentId}", paymentId);
            throw;
        }
    }

    public async Task<PaymentResponseDto?> GetPaymentByOrderIdAsync(Guid orderId)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order ID cannot be empty", nameof(orderId));

        try
        {
            var payment = await _databasePipeline.ExecuteAsync(async _ =>
                await _repository.GetByOrderIdAsync(orderId));

            return payment == null ? null : _mapper.Map<PaymentResponseDto>(payment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving payment for order {OrderId}", orderId);
            throw;
        }
    }

    public async Task<PaymentResponseDto?> RefundPaymentAsync(RefundRequestDto request)
    {
        if (request?.PaymentId == Guid.Empty)
            throw new ArgumentException("Payment ID cannot be empty");

        try
        {
            var payment = await _databasePipeline.ExecuteAsync(async _ =>
                await _repository.GetByIdAsync(request!.PaymentId));

            if (payment == null)
                return null;

            var processingResult = await _processor.RefundAsync(payment, request!.Amount);

            if (processingResult.IsSuccess)
            {
                payment.Status = PaymentStatus.Refunded;
                await _databasePipeline.ExecuteAsync(async _ =>
                    await _repository.UpdateAsync(payment));

                await InvalidatePaymentCacheAsync(payment.Id);

                await _messagingPipeline.ExecuteAsync(async _ =>
                    await PublishPaymentEventAsync("payment.refunded", payment));

                return _mapper.Map<PaymentResponseDto>(payment);
            }

            throw new InvalidOperationException($"Refund failed: {processingResult.ErrorMessage}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing refund for payment {PaymentId}", request?.PaymentId);
            throw;
        }
    }

    public async Task<PaymentResponseDto?> CancelPaymentAsync(Guid paymentId)
    {
        if (paymentId == Guid.Empty)
            throw new ArgumentException("Payment ID cannot be empty", nameof(paymentId));

        try
        {
            var payment = await _databasePipeline.ExecuteAsync(async _ =>
                await _repository.GetByIdAsync(paymentId));

            if (payment == null)
                return null;

            if (payment.Status != PaymentStatus.Pending && payment.Status != PaymentStatus.Processing)
            {
                throw new InvalidOperationException($"Cannot cancel payment with status {payment.Status}");
            }

            var processingResult = await _processor.CancelAsync(payment);

            if (processingResult.IsSuccess)
            {
                payment.Status = PaymentStatus.Cancelled;
                await AddPaymentEventAsync(payment.Id, PaymentEventType.Cancelled, "Payment cancelled by request");

                await _databasePipeline.ExecuteAsync(async _ =>
                    await _repository.UpdateAsync(payment));

                await InvalidatePaymentCacheAsync(payment.Id);

                await _messagingPipeline.ExecuteAsync(async _ =>
                    await PublishPaymentEventAsync("payment.cancelled", payment));

                return _mapper.Map<PaymentResponseDto>(payment);
            }

            throw new InvalidOperationException($"Payment cancellation failed: {processingResult.ErrorMessage}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling payment {PaymentId}", paymentId);
            throw;
        }
    }

    public async Task<IEnumerable<PaymentResponseDto>> GetPaymentHistoryAsync(Guid customerId, DateTime? from = null, DateTime? to = null)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException("Customer ID cannot be empty", nameof(customerId));

        try
        {
            var payments = await _databasePipeline.ExecuteAsync(async _ =>
                await _repository.GetPaymentHistoryAsync(customerId, from, to));

            return _mapper.Map<IEnumerable<PaymentResponseDto>>(payments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving payment history for customer {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<bool> UpdatePaymentStatusAsync(Guid paymentId, PaymentStatus status, string? reason = null)
    {
        if (paymentId == Guid.Empty)
            throw new ArgumentException("Payment ID cannot be empty", nameof(paymentId));

        try
        {
            var payment = await _databasePipeline.ExecuteAsync(async _ =>
                await _repository.GetByIdAsync(paymentId));

            if (payment == null)
                return false;

            var oldStatus = payment.Status;
            payment.Status = status;

            if (!string.IsNullOrEmpty(reason))
            {
                payment.FailureReason = reason;
            }

            var updated = await _databasePipeline.ExecuteAsync(async _ =>
                await _repository.UpdateAsync(payment));

            if (updated != null)
            {
                await AddPaymentEventAsync(paymentId, MapStatusToEventType(status),
                    $"Status changed from {oldStatus} to {status}. Reason: {reason}");

                await InvalidatePaymentCacheAsync(paymentId);

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating payment status for {PaymentId}", paymentId);
            throw;
        }
    }

    public async Task<IEnumerable<PaymentResponseDto>> GetCustomerPaymentsAsync(Guid customerId)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException("Customer ID cannot be empty", nameof(customerId));

        try
        {
            var payments = await _databasePipeline.ExecuteAsync(async _ =>
                await _repository.GetByCustomerIdAsync(customerId));

            return _mapper.Map<IEnumerable<PaymentResponseDto>>(payments);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving payments for customer {CustomerId}", customerId);
            throw;
        }
    }

    private async Task AddPaymentEventAsync(Guid paymentId, PaymentEventType eventType, string eventData)
    {
        try
        {
            var paymentEvent = new PaymentEvent
            {
                Id = Guid.NewGuid(),
                PaymentId = paymentId,
                EventType = eventType,
                EventData = eventData,
                Timestamp = DateTime.UtcNow
            };

            await _databasePipeline.ExecuteAsync(async _ =>
                await _repository.AddEventAsync(paymentEvent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add payment event for {PaymentId}", paymentId);
        }
    }

    private async Task CachePaymentAsync(Payment payment)
    {
        try
        {
            var cacheKey = $"payment:{payment.Id}";
            var dto = _mapper.Map<PaymentResponseDto>(payment);
            var serialized = JsonSerializer.Serialize(dto);

            _memoryCache.Set(cacheKey, dto, TimeSpan.FromMinutes(5));

            await _cachePipeline.ExecuteAsync(async _ =>
                await _redisCache.StringSetAsync(cacheKey, serialized, TimeSpan.FromMinutes(30)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache payment {PaymentId}", payment.Id);
        }
    }

    private async Task<PaymentResponseDto?> GetCachedPaymentAsync(Guid paymentId)
    {
        try
        {
            var cacheKey = $"payment:{paymentId}";

            if (_memoryCache.TryGetValue(cacheKey, out PaymentResponseDto? cached))
                return cached;

            var redisValue = await _cachePipeline.ExecuteAsync(async _ =>
                await _redisCache.StringGetAsync(cacheKey));

            if (redisValue.HasValue)
            {
                var deserialized = JsonSerializer.Deserialize<PaymentResponseDto>(redisValue!);
                if (deserialized != null)
                {
                    _memoryCache.Set(cacheKey, deserialized, TimeSpan.FromMinutes(5));
                    return deserialized;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get cached payment {PaymentId}", paymentId);
            return null;
        }
    }

    private async Task InvalidatePaymentCacheAsync(Guid paymentId)
    {
        try
        {
            var cacheKey = $"payment:{paymentId}";
            _memoryCache.Remove(cacheKey);

            await _cachePipeline.ExecuteAsync(async _ =>
                await _redisCache.KeyDeleteAsync(cacheKey));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate cache for payment {PaymentId}", paymentId);
        }
    }

    private async Task PublishPaymentEventAsync(string eventType, Payment payment)
    {
        try
        {
            var sendEndpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri($"queue:{eventType}"));
            await sendEndpoint.Send(new
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                CustomerId = payment.CustomerId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                Status = payment.Status.ToString(),
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish payment event {EventType} for payment {PaymentId}",
                eventType, payment.Id);
        }
    }

    private static PaymentEventType MapStatusToEventType(PaymentStatus status) => status switch
    {
        PaymentStatus.Pending => PaymentEventType.Created,
        PaymentStatus.Processing => PaymentEventType.Processing,
        PaymentStatus.Succeeded => PaymentEventType.Succeeded,
        PaymentStatus.Failed => PaymentEventType.Failed,
        PaymentStatus.Cancelled => PaymentEventType.Cancelled,
        PaymentStatus.Refunded => PaymentEventType.Refunded,
        _ => PaymentEventType.Created
    };
}