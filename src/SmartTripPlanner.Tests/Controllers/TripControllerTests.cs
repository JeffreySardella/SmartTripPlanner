namespace SmartTripPlanner.Tests.Controllers;

using SmartTripPlanner.Api.Controllers;
using SmartTripPlanner.Api.Models;
using SmartTripPlanner.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

public class TripControllerTests
{
    private readonly IAgentService _agentService = Substitute.For<IAgentService>();
    private readonly IPersistenceService _persistenceService = Substitute.For<IPersistenceService>();
    private readonly TripController _sut;

    public TripControllerTests()
    {
        var logger = Substitute.For<ILogger<TripController>>();
        _sut = new TripController(_agentService, _persistenceService, logger);
    }

    [Fact]
    public async Task PlanTrip_Success_ReturnsOkWithResponse()
    {
        _agentService.RunAsync(Arg.Any<string>(), Arg.Any<int>())
            .Returns("Here is your trip plan.");

        var result = await _sut.PlanTrip(new TripRequest { Prompt = "Plan Tokyo" });

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task PlanTrip_UnexpectedException_Returns500()
    {
        _agentService.RunAsync(Arg.Any<string>(), Arg.Any<int>())
            .ThrowsAsync(new Exception("Something broke"));

        var result = await _sut.PlanTrip(new TripRequest { Prompt = "Plan Tokyo" });

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetTrips_ReturnsList()
    {
        _persistenceService.GetTripsAsync().Returns(new List<Trip>
        {
            new() { Id = 1, Destination = "Tokyo" },
            new() { Id = 2, Destination = "Paris" }
        });

        var result = await _sut.GetTrips();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task GetTrip_Found_ReturnsTrip()
    {
        _persistenceService.GetTripByIdAsync(1).Returns(new Trip
        {
            Id = 1, Destination = "Tokyo", Events = [
                new TripEvent { Id = 1, Summary = "Temple Visit", Location = "Asakusa" }
            ]
        });

        var result = await _sut.GetTrip(1);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task GetTrip_NotFound_Returns404()
    {
        _persistenceService.GetTripByIdAsync(999).Returns((Trip?)null);

        var result = await _sut.GetTrip(999);

        Assert.IsType<NotFoundResult>(result);
    }
}
