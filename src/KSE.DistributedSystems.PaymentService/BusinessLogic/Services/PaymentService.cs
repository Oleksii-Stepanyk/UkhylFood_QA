using AutoMapper;
using KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;
using KSE.DistributedSystems.PaymentService.DataAccess.Interfaces;
using MassTransit;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;
using StackExchange.Redis;
using System.Diagnostics;

namespace KSE.DistributedSystems.PaymentService.BusinessLogic.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentProcessor _processor;
    private readonly IMapper _mapper;
    private readonly ILogger<PaymentService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly IDatabase _redisCache;
    private readonly ISendEndpointProvider _sendEndpointProvider;
    private readonly PaymentMonitoringService _monitoringService;

    public PaymentService(
        IPaymentRepository repository,
        IPaymentProcessor processor,
        IMapper mapper,
        ILogger<PaymentService> logger,
        IMemoryCache memoryCache,
        IConnectionMultiplexer redis,
        ISendEndpointProvider sendEndpointProvider,
        PaymentMonitoringService monitoringService)
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
    }

    public async Task<PaymentResponseDto> ProcessPaymentAsync(PaymentRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var success = false;
        var paymentMethod = request.PaymentMethod.ToString();

        _logger.LogInformation("Processing payment for order {OrderId}, customer {CustomerId}, amount {Amount} {Currency}",
            request.OrderId, request.CustomerId, request.Amount, request.Currency);

        var existingPayment = await _repository.GetByOrderIdAsync(request.OrderId);
        if (existingPayment != null)
        {
            _logger.LogWarning("Payment already exists for order {OrderId}", request.OrderId);
            throw new InvalidOperationException($"Payment already exists for order {request.OrderId}");
        }
        
        var payment = _mapper.Map<Payment>(request);
        payment.Id = Guid.NewGuid();

        try
        {
            var savedPayment = await _repository.AddAsync(payment);
            
            await AddPaymentEventAsync(savedPayment.Id, PaymentEventType.Created, "Payment created");
            
            savedPayment.Status = PaymentStatus.Processing;
            await _repository.UpdateAsync(savedPayment);
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

                await PublishPaymentEventAsync("payment.succeeded", savedPayment);

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

                await PublishPaymentEventAsync("payment.failed", savedPayment);

                _monitoringService.IncrementPaymentStatus("failed", paymentMethod, savedPayment.Amount);
                _monitoringService.IncrementPaymentProcessed("process_payment", "failed", paymentMethod);
            }

            await _repository.UpdateAsync(savedPayment);

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

            if (payment.Id != Guid.Empty)
            {
                try
                {
                    payment.Status = PaymentStatus.Failed;
                    payment.FailureReason = "Internal processing error";
                    await _repository.UpdateAsync(payment);
                    await AddPaymentEventAsync(payment.Id, PaymentEventType.Failed, $"Internal error: {ex.Message}");
                }
                catch (Exception updateEx)
                {
                    _logger.LogError(updateEx, "Failed to update payment status after error");
                }
            }

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

        var cached = await GetCachedPaymentAsync(paymentId);
        if (cached != null)
            return cached;

        var payment = await _repository.GetByIdAsync(paymentId);
        if (payment == null)
            return null;

        var result = _mapper.Map<PaymentResponseDto>(payment);
        
        await CachePaymentAsync(payment);

        return result;
    }

    public async Task<PaymentResponseDto?> GetPaymentByOrderIdAsync(Guid orderId)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order ID cannot be empty", nameof(orderId));

        var payment = await _repository.GetByOrderIdAsync(orderId);
        if (payment == null)
            return null;

        return _mapper.Map<PaymentResponseDto>(payment);
    }

    public async Task<IEnumerable<PaymentResponseDto>> GetCustomerPaymentsAsync(Guid customerId)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException("Customer ID cannot be empty", nameof(customerId));

        var payments = await _repository.GetByCustomerIdAsync(customerId);
        return _mapper.Map<IEnumerable<PaymentResponseDto>>(payments);
    }

    public async Task<PaymentResponseDto?> RefundPaymentAsync(RefundRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogInformation("Processing refund for payment {PaymentId}, amount: {Amount}",
            request.PaymentId, request.Amount);

        var payment = await _repository.GetByIdAsync(request.PaymentId);
        if (payment == null)
        {
            _logger.LogWarning("Payment {PaymentId} not found for refund", request.PaymentId);
            return null;
        }

        if (payment.Status != PaymentStatus.Succeeded)
        {
            _logger.LogWarning("Cannot refund payment {PaymentId} with status {Status}", 
                request.PaymentId, payment.Status);
            throw new InvalidOperationException($"Cannot refund payment with status {payment.Status}");
        }

        var refundAmount = request.Amount ?? payment.Amount;
        if (refundAmount > payment.Amount)
        {
            throw new InvalidOperationException("Refund amount cannot exceed original payment amount");
        }

        try
        {
            var processingResult = await _processor.RefundAsync(payment, refundAmount);

            if (processingResult.IsSuccess)
            {
                var isFullRefund = refundAmount == payment.Amount;
                payment.Status = isFullRefund ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
                payment.RefundedAt = DateTime.UtcNow;

                var eventType = isFullRefund ? PaymentEventType.Refunded : PaymentEventType.PartiallyRefunded;
                await AddPaymentEventAsync(payment.Id, eventType, 
                    $"Refund processed: {refundAmount:C} - {request.Reason}");

                await _repository.UpdateAsync(payment);

                await InvalidatePaymentCacheAsync(payment.Id);

                await PublishPaymentEventAsync("payment.refunded", payment);

                _logger.LogInformation("Refund completed for payment {PaymentId}", request.PaymentId);

                return _mapper.Map<PaymentResponseDto>(payment);
            }
            else
            {
                _logger.LogError("Refund failed for payment {PaymentId}: {Error}", 
                    request.PaymentId, processingResult.ErrorMessage);
                throw new InvalidOperationException($"Refund processing failed: {processingResult.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing refund for payment {PaymentId}", request.PaymentId);
            await AddPaymentEventAsync(payment.Id, PaymentEventType.Failed, $"Refund failed: {ex.Message}");
            throw;
        }
    }

    public async Task<PaymentResponseDto?> CancelPaymentAsync(Guid paymentId)
    {
        if (paymentId == Guid.Empty)
            throw new ArgumentException("Payment ID cannot be empty", nameof(paymentId));

        var payment = await _repository.GetByIdAsync(paymentId);
        if (payment == null)
            return null;

        if (payment.Status != PaymentStatus.Pending && payment.Status != PaymentStatus.Processing)
        {
            throw new InvalidOperationException($"Cannot cancel payment with status {payment.Status}");
        }

        try
        {
            var processingResult = await _processor.CancelAsync(payment);

            if (processingResult.IsSuccess)
            {
                payment.Status = PaymentStatus.Cancelled;
                await AddPaymentEventAsync(payment.Id, PaymentEventType.Cancelled, "Payment cancelled by request");
                await _repository.UpdateAsync(payment);
                await InvalidatePaymentCacheAsync(payment.Id);
                await PublishPaymentEventAsync("payment.cancelled", payment);

                return _mapper.Map<PaymentResponseDto>(payment);
            }
            else
            {
                throw new InvalidOperationException($"Payment cancellation failed: {processingResult.ErrorMessage}");
            }
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

        var payments = await _repository.GetPaymentHistoryAsync(customerId, from, to);
        return _mapper.Map<IEnumerable<PaymentResponseDto>>(payments);
    }

    public async Task<bool> UpdatePaymentStatusAsync(Guid paymentId, PaymentStatus status, string? reason = null)
    {
        if (paymentId == Guid.Empty)
            throw new ArgumentException("Payment ID cannot be empty", nameof(paymentId));

        var payment = await _repository.GetByIdAsync(paymentId);
        if (payment == null)
            return false;

        var oldStatus = payment.Status;
        payment.Status = status;
        
        if (!string.IsNullOrEmpty(reason))
        {
            payment.FailureReason = reason;
        }

        var updated = await _repository.UpdateAsync(payment);
        if (updated != null)
        {
            await AddPaymentEventAsync(paymentId, MapStatusToEventType(status), 
                $"Status changed from {oldStatus} to {status}. Reason: {reason}");
            
            await InvalidatePaymentCacheAsync(paymentId);
            
            return true;
        }

        return false;
    }

    private async Task AddPaymentEventAsync(Guid paymentId, PaymentEventType eventType, string eventData)
    {
        var paymentEvent = new PaymentEvent
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId,
            EventType = eventType,
            EventData = eventData,
            Timestamp = DateTime.UtcNow
        };

        await _repository.AddEventAsync(paymentEvent);
    }

    private async Task CachePaymentAsync(Payment payment)
    {
        var cacheKey = $"payment:{payment.Id}";
        var dto = _mapper.Map<PaymentResponseDto>(payment);
        var serialized = JsonSerializer.Serialize(dto);
        _memoryCache.Set(cacheKey, dto, TimeSpan.FromMinutes(5));
        await _redisCache.StringSetAsync(cacheKey, serialized, TimeSpan.FromMinutes(30));
    }

    private async Task<PaymentResponseDto?> GetCachedPaymentAsync(Guid paymentId)
    {
        var cacheKey = $"payment:{paymentId}";
        if (_memoryCache.TryGetValue(cacheKey, out PaymentResponseDto? cached))
            return cached;
        
        var redisValue = await _redisCache.StringGetAsync(cacheKey);
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

    private async Task InvalidatePaymentCacheAsync(Guid paymentId)
    {
        var cacheKey = $"payment:{paymentId}";
        _memoryCache.Remove(cacheKey);
        await _redisCache.KeyDeleteAsync(cacheKey);
    }

    private async Task PublishPaymentEventAsync(string eventType, Payment payment)
    {
        try
        {
            var endpoint = await _sendEndpointProvider.GetSendEndpoint(new Uri("queue:payment.response"));
            if (payment.Status == PaymentStatus.Succeeded)
            {
                await endpoint.Send(new KSE.DistributedSystems.OrderService.Models.PaymentResult(payment.OrderId, 0)); // Paid = 0
            }
            else if (payment.Status == PaymentStatus.Failed)
            {
                await endpoint.Send(new KSE.DistributedSystems.OrderService.Models.PaymentResult(payment.OrderId, 1)); // Failed = 1
            }
            else
            {
                await endpoint.Send(new KSE.DistributedSystems.OrderService.Models.PaymentResult(payment.OrderId, 2)); // Pending = 2
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish payment event {EventType} for payment {PaymentId}", 
                eventType, payment.Id);
        }
    }

    private static PaymentEventType MapStatusToEventType(PaymentStatus status) => status switch
    {
        PaymentStatus.Processing => PaymentEventType.Processing,
        PaymentStatus.Succeeded => PaymentEventType.Succeeded,
        PaymentStatus.Failed => PaymentEventType.Failed,
        PaymentStatus.Cancelled => PaymentEventType.Cancelled,
        PaymentStatus.Refunded => PaymentEventType.Refunded,
        PaymentStatus.PartiallyRefunded => PaymentEventType.PartiallyRefunded,
        _ => PaymentEventType.Created
    };
} 