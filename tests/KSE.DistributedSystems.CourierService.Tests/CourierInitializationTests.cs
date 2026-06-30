using System;
using AutoMapper;
using KSE.DistributedSystems.CourierService.DataAccess.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;
using Moq;
using OpenTelemetry.Trace;
using Xunit;

namespace KSE.DistributedSystems.CourierService.Tests;

[Trait("Category", "Unit")]
public class CourierInitializationTests
{
    [Fact]
    public void Constructor_WithNullRepository_ThrowsArgumentNullException()
    {
        // arrange
        ICourierRepository nullRepo = null!;
        var mapper = new Mock<IMapper>().Object;
        var endpoint = new Mock<ISendEndpointProvider>().Object;
        var logger = new Mock<ILogger<BusinessLogic.Services.CourierService>>().Object;
        var tracer = TracerProvider.Default.GetTracer("Dummy");

        // act & assert
        var ex = Assert.Throws<ArgumentNullException>(() => 
            new BusinessLogic.Services.CourierService(nullRepo, mapper, endpoint, logger, tracer));
        Assert.Equal("repository", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullMapper_ThrowsArgumentNullException()
    {
        // arrange
        var repo = new Mock<ICourierRepository>().Object;
        IMapper nullMapper = null!;
        var endpoint = new Mock<ISendEndpointProvider>().Object;
        var logger = new Mock<ILogger<BusinessLogic.Services.CourierService>>().Object;
        var tracer = TracerProvider.Default.GetTracer("Dummy");

        // act & assert
        var ex = Assert.Throws<ArgumentNullException>(() => 
            new BusinessLogic.Services.CourierService(repo, nullMapper, endpoint, logger, tracer));
        Assert.Equal("mapper", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithNullSendEndpointProvider_ThrowsArgumentNullException()
    {
        // arrange
        var repo = new Mock<ICourierRepository>().Object;
        var mapper = new Mock<IMapper>().Object;
        ISendEndpointProvider nullEndpoint = null!;
        var logger = new Mock<ILogger<BusinessLogic.Services.CourierService>>().Object;
        var tracer = TracerProvider.Default.GetTracer("Dummy");

        // act & assert
        var ex = Assert.Throws<ArgumentNullException>(() => 
            new BusinessLogic.Services.CourierService(repo, mapper, nullEndpoint, logger, tracer));
        Assert.Equal("sendEndpointProvider", ex.ParamName);
    }
}
