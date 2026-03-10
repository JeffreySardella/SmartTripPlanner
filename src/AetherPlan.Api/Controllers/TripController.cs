namespace AetherPlan.Api.Controllers;

using AetherPlan.Api.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class TripController(IAgentService agentService) : ControllerBase
{
    [HttpPost("plan")]
    public async Task<IActionResult> PlanTrip([FromBody] TripRequest request)
    {
        var result = await agentService.RunAsync(request.Prompt);
        return Ok(new { response = result });
    }
}

public class TripRequest
{
    public required string Prompt { get; set; }
}
