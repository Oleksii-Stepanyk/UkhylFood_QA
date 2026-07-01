using Xunit;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using KSE.DistributedSystems.PaymentService.BusinessLogic.DTOs;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Interfaces;
using KSE.DistributedSystems.PaymentService.BusinessLogic.MappingProfiles;
using KSE.DistributedSystems.PaymentService.BusinessLogic.Services;
using KSE.DistributedSystems.PaymentService.DataAccess.Entities;
using KSE.DistributedSystems.PaymentService.DataAccess.Interfaces;

namespace KSE.DistributedSystems.PaymentService.Tests.ContractTests
{
    [Trait("Category", "Contract")]
    public class PaymentResultProducerContractTests : IAsyncLifetime
    {
        private InMemoryTestHarness _harness;
        private Mock<IPaymentRepository> _repositoryMock;
        private Mock<IPaymentProcessor> _processorMock;
        private Mock<ILogger<BusinessLogic.Services.PaymentService>> _loggerMock;
        private IMemoryCache _memoryCache;
        private Mock<StackExchange.Redis.IConnectionMultiplexer> _redisMock;
        private Mock<StackExchange.Redis.IDatabase> _databaseMock;
        private IMapper _mapper;
        private BusinessLogic.Services.PaymentService _paymentService;

        public async Task InitializeAsync()
        {
            _harness = new InMemoryTestHarness();
            await _harness.Start();

            _repositoryMock = new Mock<IPaymentRepository>();
            _processorMock = new Mock<IPaymentProcessor>();
            _loggerMock = new Mock<ILogger<BusinessLogic.Services.PaymentService>>();
            _memoryCache = new MemoryCache(new MemoryCacheOptions());
            _redisMock = new Mock<StackExchange.Redis.IConnectionMultiplexer>();
            _databaseMock = new Mock<StackExchange.Redis.IDatabase>();

            var configuration = new MapperConfiguration(cfg => cfg.AddProfile<PaymentProfile>());
            _mapper = configuration.CreateMapper();

            _redisMock.Setup(x => x.IsConnected).Returns(true);
            _redisMock.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_databaseMock.Object);

            var monitorService = new PaymentMonitoringService(new Mock<ILogger<PaymentMonitoringService>>().Object);

            _paymentService = new BusinessLogic.Services.PaymentService(
                _repositoryMock.Object,
                _processorMock.Object,
                _mapper,
                _loggerMock.Object,
                _memoryCache,
                _redisMock.Object,
                _harness.Bus,
                monitorService);
        }

        public async Task DisposeAsync()
        {
            _memoryCache?.Dispose();
            await _harness.Stop();
            _harness.Dispose();
        }

        [Fact]
        public async Task ProcessPaymentAsync_ShouldSendPaymentResultContract()
        {
            // Arrange
            var request = new PaymentRequestDto
            {
                OrderId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                Amount = 100.50m,
                Currency = "USD",
                PaymentMethod = PaymentMethod.CreditCard
            };

            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                OrderId = request.OrderId,
                CustomerId = request.CustomerId,
                Amount = request.Amount,
                Currency = request.Currency,
                PaymentMethod = request.PaymentMethod,
                Status = PaymentStatus.Processing
            };

            var processingResult = new PaymentProcessingResult
            {
                IsSuccess = true,
                ExternalPaymentId = "ext_123456",
                ProcessorResponse = new Dictionary<string, string>()
            };

            _repositoryMock.Setup(x => x.GetByOrderIdAsync(request.OrderId))
                .ReturnsAsync((Payment?)null);
            _repositoryMock.Setup(x => x.AddAsync(It.IsAny<Payment>()))
                .ReturnsAsync(payment);
            _repositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Payment>()))
                .ReturnsAsync(payment);
            _repositoryMock.Setup(x => x.AddEventAsync(It.IsAny<PaymentEvent>()))
                .Returns(Task.CompletedTask);
            _processorMock.Setup(x => x.ProcessAsync(It.IsAny<Payment>()))
                .ReturnsAsync(processingResult);

            // Act
            await _paymentService.ProcessPaymentAsync(request);

            // Assert contract
            Assert.True(await _harness.Sent.Any<KSE.DistributedSystems.OrderService.Models.PaymentResult>());
        }
    }
}
