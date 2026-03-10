namespace AetherPlan.Tests.Controllers;

using AetherPlan.Api.Controllers;
using AetherPlan.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

public class TripControllerTests
{
    private readonly IAgentService _agentService = Substitute.For<IAgentService>();
    private readonly TripController _sut;

    public TripControllerTests()
    {
        var logger = Substitute.For<ILogger<TripController>>();
        _sut = new TripController(_agentService, logger);
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
}
