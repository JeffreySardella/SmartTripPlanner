namespace SmartTripPlanner.Tests.Controllers;

using SmartTripPlanner.Api.Controllers;
using SmartTripPlanner.Api.Models;
using SmartTripPlanner.Api.Services;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

public class LocationsControllerTests
{
    private readonly ILocationService _locationService;
    private readonly LocationsController _controller;

    public LocationsControllerTests()
    {
        _locationService = Substitute.For<ILocationService>();
        _controller = new LocationsController(_locationService);
    }

    [Fact]
    public async Task SaveLocation_ValidRequest_Returns201WithLocation()
    {
        var request = new SaveLocationRequest { Name = "Test", Category = "cafe" };
        var saved = new CachedLocation { Id = 1, Name = "Test", Category = "cafe" };
        _locationService.SaveLocationAsync(request).Returns(saved);

        var result = await _controller.SaveLocation(request);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal(saved, created.Value);
    }

    [Fact]
    public async Task SaveLocation_LlmExtractionFails_Returns422()
    {
        var request = new SaveLocationRequest { RawPageContent = "no location here" };
        _locationService.SaveLocationAsync(request)
            .ThrowsAsync(new InvalidOperationException("Could not extract"));

        var result = await _controller.SaveLocation(request);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        Assert.Equal(422, unprocessable.StatusCode);
    }

    [Fact]
    public async Task SaveLocation_MissingNameAndContent_Returns400()
    {
        var request = new SaveLocationRequest();
        _locationService.SaveLocationAsync(request)
            .ThrowsAsync(new ArgumentException("Either Name or RawPageContent must be provided."));

        var result = await _controller.SaveLocation(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task GetLocations_NoFilters_ReturnsAll()
    {
        var locations = new List<CachedLocation>
        {
            new() { Id = 1, Name = "A", Category = "cafe" },
            new() { Id = 2, Name = "B", Category = "restaurant" }
        };
        _locationService.GetLocationsAsync(null, false).Returns(locations);

        var result = await _controller.GetLocations(null, false);

        var ok = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<List<CachedLocation>>(ok.Value);
        Assert.Equal(2, returned.Count);
    }

    [Fact]
    public async Task GetLocations_UnassignedOnly_PassesFilterToService()
    {
        _locationService.GetLocationsAsync(null, true).Returns(new List<CachedLocation>());

        await _controller.GetLocations(null, true);

        await _locationService.Received(1).GetLocationsAsync(null, true);
    }

    [Fact]
    public async Task AssignLocation_ValidIds_Returns200()
    {
        var request = new AssignLocationRequest { TripId = 1 };
        var updated = new CachedLocation { Id = 5, Name = "Place", Category = "other", TripId = 1 };
        _locationService.AssignToTripAsync(5, 1).Returns(updated);

        var result = await _controller.AssignLocation(5, request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(updated, ok.Value);
    }

    [Fact]
    public async Task AssignLocation_NotFound_Returns404()
    {
        var request = new AssignLocationRequest { TripId = 1 };
        _locationService.AssignToTripAsync(999, 1)
            .ThrowsAsync(new KeyNotFoundException("Location 999 not found."));

        var result = await _controller.AssignLocation(999, request);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }
}
