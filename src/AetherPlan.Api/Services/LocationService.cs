namespace AetherPlan.Api.Services;

using System.Text.Json;
using AetherPlan.Api.Data;
using AetherPlan.Api.Models;
using Microsoft.EntityFrameworkCore;

public class LocationService(AetherPlanDbContext db, ILlmClient llmClient) : ILocationService
{
    private const int MaxRawContentInputLength = 10_000;
    private const int MaxRawContentLength = 2000;

    public async Task<CachedLocation> SaveLocationAsync(SaveLocationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.RawPageContent) && request.RawPageContent.Length > MaxRawContentInputLength)
            throw new ArgumentException($"RawPageContent exceeds maximum length of {MaxRawContentInputLength} characters.");

        var name = request.Name;
        var address = request.Address;
        var category = request.Category;

        if (string.IsNullOrWhiteSpace(name))
        {
            if (string.IsNullOrWhiteSpace(request.RawPageContent))
                throw new ArgumentException("Either Name or RawPageContent must be provided.");

            var extracted = await ExtractWithLlmAsync(request.RawPageContent);
            name = extracted.Name;
            address ??= extracted.Address;
            category ??= extracted.Category;

            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Could not extract location name from page content.");
        }

        var location = new CachedLocation
        {
            Name = name,
            Address = address,
            Latitude = request.Latitude ?? 0,
            Longitude = request.Longitude ?? 0,
            Category = category ?? "other",
            SourceUrl = request.SourceUrl,
            LastUpdated = DateTime.UtcNow
        };

        db.CachedLocations.Add(location);
        await db.SaveChangesAsync();
        return location;
    }

    public async Task<List<CachedLocation>> GetLocationsAsync(int? tripId, bool unassignedOnly)
    {
        var query = db.CachedLocations.AsQueryable();

        if (tripId.HasValue)
            query = query.Where(l => l.TripId == tripId.Value);
        else if (unassignedOnly)
            query = query.Where(l => l.TripId == null);

        return await query.OrderBy(l => l.Name).ToListAsync();
    }

    public async Task<CachedLocation> AssignToTripAsync(int locationId, int tripId)
    {
        var location = await db.CachedLocations.FindAsync(locationId)
            ?? throw new KeyNotFoundException($"Location {locationId} not found.");

        var tripExists = await db.Trips.AnyAsync(t => t.Id == tripId);
        if (!tripExists)
            throw new KeyNotFoundException($"Trip {tripId} not found.");

        location.TripId = tripId;
        await db.SaveChangesAsync();
        return location;
    }

    private async Task<ExtractedLocation> ExtractWithLlmAsync(string rawContent)
    {
        var truncated = rawContent.Length > MaxRawContentLength
            ? rawContent[..MaxRawContentLength]
            : rawContent;

        var prompt = $$"""
            Extract location information from this webpage text. Respond with ONLY a JSON object, no other text:
            {"name": "...", "address": "...", "category": "..."}

            Category should be one of: restaurant, hotel, attraction, bar, cafe, shop, park, museum, other.
            If you cannot determine a field, use null.

            Webpage text:
            {{truncated}}
            """;

        var messages = new List<LlmMessage>
        {
            new() { Role = "user", Content = prompt }
        };

        var response = await llmClient.ChatAsync(messages);
        var content = response.Message.Content ?? "";

        // Extract JSON from response (LLM may wrap in markdown code blocks)
        var jsonStart = content.IndexOf('{');
        var jsonEnd = content.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd < 0)
            return new ExtractedLocation();

        var jsonStr = content[jsonStart..(jsonEnd + 1)];

        try
        {
            return JsonSerializer.Deserialize<ExtractedLocation>(jsonStr,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new ExtractedLocation();
        }
        catch (JsonException)
        {
            return new ExtractedLocation();
        }
    }

    private class ExtractedLocation
    {
        public string? Name { get; set; }
        public string? Address { get; set; }
        public string? Category { get; set; }
    }
}
