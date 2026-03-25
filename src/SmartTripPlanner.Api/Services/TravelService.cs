namespace SmartTripPlanner.Api.Services;

using SmartTripPlanner.Api.Models;

public class TravelService : ITravelService
{
    private const double EarthRadiusKm = 6371.0;
    private const double MphToKmh = 1.60934;

    public double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusKm * c;
    }

    public double EstimateTravelMinutes(double distanceKm, double averageSpeedMph = 40)
    {
        var speedKmh = averageSpeedMph * MphToKmh;
        return (distanceKm / speedKmh) * 60;
    }

    public TravelValidation ValidateTravel(double lat1, double lon1, double lat2, double lon2,
        DateTime departureTime, DateTime arrivalDeadline)
    {
        var distanceKm = CalculateDistanceKm(lat1, lon1, lat2, lon2);
        var estimatedMinutes = EstimateTravelMinutes(distanceKm);
        var availableMinutes = (arrivalDeadline - departureTime).TotalMinutes;

        return new TravelValidation
        {
            DistanceKm = Math.Round(distanceKm, 2),
            EstimatedMinutes = Math.Round(estimatedMinutes, 2),
            AvailableMinutes = Math.Round(availableMinutes, 2),
            IsFeasible = estimatedMinutes <= availableMinutes
        };
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
