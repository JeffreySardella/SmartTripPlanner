namespace AetherPlan.Api.Services;

using AetherPlan.Api.Models;

public interface ILocationService
{
    Task<CachedLocation> SaveLocationAsync(SaveLocationRequest request);
    Task<List<CachedLocation>> GetLocationsAsync(int? tripId, bool unassignedOnly);
    Task<CachedLocation> AssignToTripAsync(int locationId, int tripId);
}
