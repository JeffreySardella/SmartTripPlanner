namespace AetherPlan.Api.Services;

using AetherPlan.Api.Models;

public interface ITravelService
{
    double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2);
    double EstimateTravelMinutes(double distanceKm, double averageSpeedMph = 40);
    TravelValidation ValidateTravel(double lat1, double lon1, double lat2, double lon2,
        DateTime departureTime, DateTime arrivalDeadline);
}
