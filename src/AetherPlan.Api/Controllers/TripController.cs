namespace AetherPlan.Api.Controllers;

using AetherPlan.Api.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class TripController(IAgentService agentService, ILogger<TripController> logger) : ControllerBase
{
    [HttpPost("plan")]
    public async Task<IActionResult> PlanTrip([FromBody] TripRequest request)
    {
        try
        {
            var result = await agentService.RunAsync(request.Prompt);
            return Ok(new { response = result });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process trip request");
            return StatusCode(500, new { error = "An internal error occurred. Please try again." });
        }
    }
}

public class TripRequest
{
    public required string Prompt { get; set; }
}
