# Unit Test Generation Strategy — OrderService

## Scope

This strategy covers unit test generation for `KSE.DistributedSystems.OrderService`.
The system under test (SUT) is the `OrderService` class which orchestrates order lifecycle
through 4 public methods: `OnOrderPlaced`, `OnPaymentSuccess`, `OnPaymentFail`, `OnOrderUpdate`.

## Technology Stack

| Concern        | Tool                    |
|----------------|-------------------------|
| Test framework | xUnit 2.x               |
| Mocking        | Moq                     |
| Assertions     | xUnit `Assert.*`        |
| Target runtime | .NET 8.0                |

## Architecture Conventions

### File & Class Layout

- **One fixture class per file.** File name = class name (e.g. `OrderPlacementTests.cs`).
- **Shared base class** `OrderServiceTestBase` in `OrderServiceTestBase.cs` owns all mock
  declarations and SUT construction. Every fixture inherits from it.
- **Namespace**: `KSE.DistributedSystems.OrderService.Tests` for all unit tests.
- **Trait**: Mark every fixture with `[Trait("Category", "Unit")]` so tests can be filtered
  with `dotnet test --filter "Category=Unit"`.

### Naming

Test methods follow **`MethodUnderTest_ExpectedBehavior_Condition`**:

```
OnOrderPlaced_ShouldThrowPaymentNotFoundException_WhenNoPaymentMethodExists
OnPaymentSuccess_ShouldUpdateInvoiceStatus
OnPaymentFail_ShouldPublishPaymentFailedEvent
```

### Variable Naming

**All variable names must be at least 2 meaningful symbols long.** Single-letter names
like `r`, `o`, `p`, `i` are forbidden — even inside lambda expressions and `Setup`/`Verify`
callbacks.

✅ Good:
```csharp
MockOrderRepository.Setup(repository => repository.CreateOrderAsync(It.IsAny<Order>()))
MockPublishEndpoint.Verify(publisher => publisher.Publish(order, default), Times.Once);
```

❌ Bad:
```csharp
_mockOrderRepository.Setup(r => r.CreateOrderAsync(It.IsAny<Order>()))
_mockPublishEndpoint.Verify(p => p.Publish(o, default), Times.Once);
_mockInvoiceRepository.Verify(r => r.SaveInvoice(It.Is<Invoice>(i => i.OrderId == order.Id)))
```

### Test Structure (AAA)

Every test must follow Arrange → Act → Assert. Keep each section visually distinct
(blank line between sections). Do NOT add `// Arrange`, `// Act`, `// Assert` comments —
the structure should be self-evident from the code.

## Base Class Contract

`OrderServiceTestBase` exposes the following protected members for all fixtures:

```csharp
protected readonly Mock<IPublishEndpoint>                    MockPublishEndpoint;
protected readonly Mock<IOrderRepository>                    MockOrderRepository;
protected readonly Mock<IPaymentRepository>                  MockPaymentRepository;
protected readonly Mock<IInvoiceRepository>                  MockInvoiceRepository;
protected readonly Mock<ILogger<OrderMonitoringService>>     MockLogger;
protected readonly OrderMonitoringService                    MetricsService;
protected readonly Services.OrderService                     OrderService;  // the SUT
```

Constructor instantiates `MetricsService` with `MockLogger.Object` and `OrderService`
with all `.Object` mocks. xUnit creates a **fresh instance** of the fixture per `[Fact]`,
so every test gets clean mocks — no shared state leaks.

## Fixture Definitions

### Fixture 1 — `OrderPlacementTests`
**SUT method**: `OnOrderPlaced(Order, IPublishEndpoint)`

| # | Test name | What it verifies |
|---|-----------|------------------|
| 1 | `_ShouldThrowPaymentNotFoundException_WhenNoPaymentMethodExists` | `GetPaymentMethodByCustomerId` returns `null` → `PaymentNotFoundException` thrown, `SaveInvoice` never called |
| 2 | `_ShouldSetStatusesToPending` | Statuses are overwritten to `Pending` (set initial to non-Pending to prove mutation), full workflow completes (`CreateOrderAsync` + `SaveInvoice` called) |
| 3 | `_ShouldSaveInvoice_WhenSuccessful` | Invoice saved with correct `OrderId`, `CustomerId`, `TotalPrice`, `Currency="USD"`, `PaymentStatus=Pending` |

### Fixture 2 — `PaymentSuccessTests`
**SUT method**: `OnPaymentSuccess(PaymentResult, IPublishEndpoint)`

| # | Test name | What it verifies |
|---|-----------|------------------|
| 1 | `_ShouldUpdateInvoiceStatus` | `UpdateInvoiceStatus` called with correct `OrderId` and `PaymentStatus.Paid` |
| 2 | `_ShouldUpdateOrderStatus` | `UpdateOrderStatusAsync` called with resolved `orderId` and `PaymentStatus.Paid` |
| 3 | `_ShouldPublishUpdatedOrderEvent` | `publishEndpoint.Publish(order)` called exactly once with the updated order |

### Fixture 3 — `PaymentFailTests`
**SUT method**: `OnPaymentFail(PaymentResult, IPublishEndpoint)`

| # | Test name | What it verifies |
|---|-----------|------------------|
| 1 | `_ShouldUpdateInvoiceStatusToFailed` | `UpdateInvoiceStatus` called with `PaymentStatus.Failed` |
| 2 | `_ShouldUpdateOrderStatusToFailed` | `UpdateOrderStatusAsync` called with `PaymentStatus.Failed` |
| 3 | `_ShouldPublishPaymentFailedEvent` | `Publish` called with strongly-typed `PaymentFailed` DTO containing correct `OrderId` and `Status="Failed"` |

## Rules for Test Generation

### Mock Setup

1. **Only set up mocks that the test path exercises.** If `OnPaymentFail` never calls
   `GetPaymentMethodByCustomerId`, do not set it up.
2. Use `It.IsAny<T>()` for arguments you don't care about; use exact values for
   arguments you're asserting on.
3. Repository mocks return domain objects. Never return `null` from a mock unless
   you're testing the null-handling path.

### Assertions

1. **State assertions** (`Assert.Equal`) for verifying mutations on domain objects.
2. **Interaction assertions** (`Mock.Verify`) for verifying repository calls and
   event publishing. Always specify `Times.Once` or `Times.Never` — never leave it
   as default.
3. **Use strongly-typed generics** in `Verify` calls. Never do
   `It.Is<object>(o => o.GetType().Name == "...")`. Use `It.Is<PaymentFailed>(pf => ...)`.
4. **One logical assertion per test.** A test can have multiple `Assert` calls if they
   verify the same behavior (e.g. checking both `Status` and `PaymentStatus` of the same
   object). But do not mix "did the invoice get saved?" and "was the event published?" in
   the same test.

### Data Construction

1. **Always use `Guid.NewGuid()`** for IDs — never hardcode GUIDs.
2. **Set initial state to non-default values** when testing mutations.
   Example: set `Status = OrderStatus.Confirmed` before calling `OnOrderPlaced` to prove
   it gets overwritten to `Pending`.
3. **Initialize `Items = []`** on `Order` objects passed to `OnPaymentSuccess` since
   the production code calls `order.Items.ForEach(...)` and will NRE otherwise.
4. **`PaymentResult`** is a plain class with `OrderId` (Guid) and `Status` (int).
   Cast `PaymentStatus` enum to `int` when constructing: `Status = (int)PaymentStatus.Paid`.

### What NOT to Test

- **Model property getters/setters** — these are auto-properties with no logic.
  Tests like `Order_ShouldHaveValidInitialState` assert that `order.Id == order.Id`,
  which adds zero value.
- **Enum existence** — testing that `PaymentMethod.Type.Cash` exists is compile-time verified.
- **`OrderMonitoringService` calls** — these are observability side-effects. Mocking the
  metrics service is sufficient for preventing test failures; verifying `.IncrementOrderProcessed`
  was called is testing implementation, not behavior.
- **Console.WriteLine** — these are debug statements in production code, not testable behavior.

### What TO Test (Priority Order)

1. **Happy path** — the full workflow completes and all expected side-effects happen.
2. **Error paths** — exceptions thrown under specific conditions (e.g. missing payment method).
3. **State transitions** — domain object statuses change correctly.
4. **Event publishing** — correct events with correct payloads are published to the message bus.
5. **Negative assertions** — certain calls should NOT happen (e.g. `SaveInvoice` not called
   when payment method is missing).

## Fixture 4 — `OrderUpdateTests`
**SUT method**: `OnOrderUpdate(Order, IPublishEndpoint)`
**Inherits**: `OrderServiceTestBase`

| # | Test name | What it verifies |
|---|-----------|------------------|
| 1 | `_ShouldCallUpdateOrderAsync` | `UpdateOrderAsync` called with the input order |
| 2 | `_ShouldNotPublishEvent` | `Publish` is never called (publish was intentionally removed to prevent event loops) |

---

## Consumer Test Fixtures

Consumer fixtures test **message routing logic**, not service internals. They have a
fundamentally different architecture from service-level fixtures.

### Consumer Fixture Architecture

- **Do NOT inherit from `OrderServiceTestBase`.** Consumer fixtures have their own
  constructor because they mock `IOrderService` (the interface), not individual repositories.
- Each fixture declares its own `Mock<IOrderService>`, `Mock<ILogger<T>>`,
  `Mock<ConsumeContext<T>>`, and instantiates the consumer directly.
- Consumer tests verify which `IOrderService` method gets called (routing), not what
  happens inside that method (that's covered by service-level fixtures).

### Exception Behavior

Consumers have two distinct exception-handling patterns. Tests MUST match the actual behavior:

| Consumer | Exception behavior | Test assertion |
|----------|-------------------|----------------|
| `CourierOrderConsumer` | **Swallows** (no `throw;`) | `await consumer.Consume(...)` — should not throw |
| `RestaurantOrderConsumer` | **Swallows** (no `throw;`) | `await consumer.Consume(...)` — should not throw |
| `PaymentResultConsumer` | **Rethrows** (`throw;`) | `Assert.ThrowsAsync<Exception>(...)` |
| `CustomerOrderConsumer` | **Rethrows** (`throw;`) | `Assert.ThrowsAsync<Exception>(...)` |

### Routing Verification

For consumers with switch/branching logic (e.g. `PaymentResultConsumer`), use
`VerifyNoOtherCalls()` after verifying the expected method call to ensure no
unintended service methods were invoked:

```csharp
_mockOrderService.Verify(service => service.OnPaymentFail(result, _mockContext.Object), Times.Once);
_mockOrderService.VerifyNoOtherCalls();
```

### ILogger Verification

When verifying that `ILogger<T>.LogError` was called (e.g. in swallow-exception tests),
use this canonical snippet:

```csharp
_mockLogger.Verify(
    logger => logger.Log(
        LogLevel.Error,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((value, type) => true),
        It.IsAny<Exception>(),
        It.Is<Func<It.IsAnyType, Exception?, string>>((value, type) => true)),
    Times.Once);
```

### Fixture 5 — `PaymentResultConsumerTests`
**SUT**: `PaymentResultConsumer` (routes by `PaymentStatus` switch)
**Inherits**: standalone

| # | Test name | What it verifies |
|---|-----------|------------------|
| 1 | `_ShouldCallOnPaymentFail_WhenStatusIsFailed` | Routes to `OnPaymentFail`, no other calls |
| 2 | `_ShouldCallOnPaymentSuccess_WhenStatusIsPaid` | Routes to `OnPaymentSuccess`, no other calls |
| 3 | `_ShouldNotCallService_WhenStatusIsPending` | No service method called for `Pending` status |
| 4 | `_ShouldNotCallService_WhenMessageIsNull` | Null message → early return, no service calls |
| 5 | `_ShouldRethrowException_WhenServiceThrows` | Exception propagates up (for MassTransit retry) |

### Fixture 6 — `CustomerOrderConsumerTests`
**SUT**: `CustomerOrderConsumer` (routes by `ExistsAsync` + `OrderStatus`)
**Inherits**: standalone

| # | Test name | What it verifies |
|---|-----------|------------------|
| 1 | `_ShouldCallOnOrderPlaced_WhenOrderDoesNotExistAndStatusIsPending` | New pending order → `OnOrderPlaced`, not `OnOrderUpdate` |
| 2 | `_ShouldCallOnOrderUpdate_WhenOrderExists` | Existing order → `OnOrderUpdate`, not `OnOrderPlaced` |
| 3 | `_ShouldIgnore_WhenOrderDoesNotExistAndStatusIsNotPending` | Non-pending new order → no service calls |
| 4 | `_ShouldRethrowException_WhenServiceThrows` | Exception propagates up |

### Fixture 7 — `CourierOrderConsumerTests`
**SUT**: `CourierOrderConsumer` (always calls `OnOrderUpdate`)
**Inherits**: standalone

| # | Test name | What it verifies |
|---|-----------|------------------|
| 1 | `_ShouldCallOnOrderUpdate` | `OnOrderUpdate` called with the order from context |
| 2 | `_ShouldNotRethrowException_WhenServiceThrows` | Exception swallowed, logger called with `LogLevel.Error` |

### Fixture 8 — `RestaurantOrderConsumerTests`
**SUT**: `RestaurantOrderConsumer` (always calls `OnOrderUpdate`)
**Inherits**: standalone

| # | Test name | What it verifies |
|---|-----------|------------------|
| 1 | `_ShouldCallOnOrderUpdate` | `OnOrderUpdate` called with the order from context |
| 2 | `_ShouldNotRethrowException_WhenServiceThrows` | Exception swallowed, logger called with `LogLevel.Error` |
