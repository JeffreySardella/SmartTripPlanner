namespace AetherPlan.Api.Controllers;

using AetherPlan.Api.Models;
using AetherPlan.Api.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
public class LocationsController(ILocationService locationService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> SaveLocation([FromBody] SaveLocationRequest request)
    {
        try
        {
            var location = await locationService.SaveLocationAsync(request);
            return CreatedAtAction(nameof(GetLocations), new { }, location);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetLocations(
        [FromQuery] int? tripId,
        [FromQuery] bool unassigned = false)
    {
        var locations = await locationService.GetLocationsAsync(tripId, unassigned);
        return Ok(locations);
    }

    [HttpPost("{id:int}/assign")]
    public async Task<IActionResult> AssignLocation(int id, [FromBody] AssignLocationRequest request)
    {
        try
        {
            var location = await locationService.AssignToTripAsync(id, request.TripId);
            return Ok(location);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
