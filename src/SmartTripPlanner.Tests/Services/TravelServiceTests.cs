namespace SmartTripPlanner.Tests.Services;

using SmartTripPlanner.Api.Services;

public class TravelServiceTests
{
    private readonly TravelService _sut = new();

    [Fact]
    public void CalculateDistanceKm_NewYorkToLA_ReturnsApprox3940()
    {
        // NYC: 40.7128, -74.0060  LA: 34.0522, -118.2437
        var distance = _sut.CalculateDistanceKm(40.7128, -74.0060, 34.0522, -118.2437);
        Assert.InRange(distance, 3900, 3980);
    }

    [Fact]
    public void CalculateDistanceKm_SamePoint_ReturnsZero()
    {
        var distance = _sut.CalculateDistanceKm(51.5074, -0.1278, 51.5074, -0.1278);
        Assert.Equal(0, distance);
    }

    [Fact]
    public void CalculateDistanceKm_LondonToParis_ReturnsApprox344()
    {
        // London: 51.5074, -0.1278  Paris: 48.8566, 2.3522
        var distance = _sut.CalculateDistanceKm(51.5074, -0.1278, 48.8566, 2.3522);
        Assert.InRange(distance, 330, 360);
    }

    [Fact]
    public void EstimateTravelMinutes_100Km_Returns93AtDefaultSpeed()
    {
        // 100km at 40mph (64.37 km/h) = ~93 minutes
        var minutes = _sut.EstimateTravelMinutes(100);
        Assert.InRange(minutes, 90, 96);
    }

    [Fact]
    public void ValidateTravel_EnoughTime_ReturnsFeasible()
    {
        // London to Paris (~344km), ~320 min travel, given 6 hours (360 min)
        var result = _sut.ValidateTravel(
            51.5074, -0.1278, 48.8566, 2.3522,
            DateTime.Today.AddHours(8),
            DateTime.Today.AddHours(14));

        Assert.True(result.IsFeasible);
    }

    [Fact]
    public void ValidateTravel_NotEnoughTime_ReturnsNotFeasible()
    {
        // NYC to LA (~3940km), would need ~61 hours, given only 2 hours
        var result = _sut.ValidateTravel(
            40.7128, -74.0060, 34.0522, -118.2437,
            DateTime.Today.AddHours(8),
            DateTime.Today.AddHours(10));

        Assert.False(result.IsFeasible);
    }
}
