namespace SmartTripPlanner.Api.Services;

using System.Text.Json;

/// <summary>
/// Searches OpenStreetMap via the Overpass API for real restaurants, hotels, and POIs.
/// Free, no API key needed.
/// </summary>
public class PoiService(IHttpClientFactory httpClientFactory, ILogger<PoiService> logger)
{
    private const string OverpassUrl = "https://overpass-api.de/api/interpreter";

    public async Task<object> SearchRestaurantsAsync(double lat, double lon, int radiusMeters = 2000, int limit = 10)
    {
        return await SearchPoiAsync(lat, lon, "amenity", "restaurant|cafe|fast_food|bar", radiusMeters, limit);
    }

    public async Task<object> SearchHotelsAsync(double lat, double lon, int radiusMeters = 5000, int limit = 10)
    {
        return await SearchPoiAsync(lat, lon, "tourism", "hotel|hostel|guest_house|motel", radiusMeters, limit);
    }

    private async Task<object> SearchPoiAsync(double lat, double lon, string key, string values, int radius, int limit)
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);

            // Build Overpass QL query for nodes/ways with the given tag within radius
            var valuesFilter = string.Join("|", values.Split('|').Select(v => v.Trim()));
            var query = $"""
                [out:json][timeout:10];
                (
                  node["{key}"~"{valuesFilter}"](around:{radius},{lat},{lon});
                  way["{key}"~"{valuesFilter}"](around:{radius},{lat},{lon});
                );
                out center {limit};
                """;

            var content = new FormUrlEncodedContent([new("data", query)]);
            var response = await client.PostAsync(OverpassUrl, content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            if (!json.TryGetProperty("elements", out var elements))
                return new { results = Array.Empty<object>(), count = 0 };

            var results = new List<object>();
            foreach (var el in elements.EnumerateArray())
            {
                var tags = el.TryGetProperty("tags", out var t) ? t : default;
                var name = GetTag(tags, "name");
                if (string.IsNullOrEmpty(name)) continue;

                // Get coordinates — nodes have lat/lon directly, ways have center
                double elLat = 0, elLon = 0;
                if (el.TryGetProperty("lat", out var latProp)) elLat = latProp.GetDouble();
                else if (el.TryGetProperty("center", out var center))
                {
                    elLat = center.TryGetProperty("lat", out var cLat) ? cLat.GetDouble() : 0;
                    elLon = center.TryGetProperty("lon", out var cLon) ? cLon.GetDouble() : 0;
                }
                if (el.TryGetProperty("lon", out var lonProp)) elLon = lonProp.GetDouble();

                results.Add(new
                {
                    name,
                    latitude = elLat,
                    longitude = elLon,
                    type = GetTag(tags, key),
                    cuisine = GetTag(tags, "cuisine"),
                    address = BuildAddress(tags),
                    phone = GetTag(tags, "phone"),
                    website = GetTag(tags, "website"),
                    stars = GetTag(tags, "stars")
                });
            }

            return new { results, count = results.Count };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Overpass search failed for {Key} near {Lat},{Lon}", key, lat, lon);
            return new { error = $"POI search failed: {ex.Message}", results = Array.Empty<object>() };
        }
    }

    private static string? GetTag(JsonElement tags, string key)
    {
        if (tags.ValueKind == JsonValueKind.Undefined) return null;
        return tags.TryGetProperty(key, out var val) ? val.GetString() : null;
    }

    private static string? BuildAddress(JsonElement tags)
    {
        if (tags.ValueKind == JsonValueKind.Undefined) return null;
        var street = GetTag(tags, "addr:street");
        var number = GetTag(tags, "addr:housenumber");
        var city = GetTag(tags, "addr:city");
        var parts = new[] { number, street, city }.Where(p => !string.IsNullOrEmpty(p));
        var addr = string.Join(", ", parts);
        return string.IsNullOrEmpty(addr) ? null : addr;
    }
}
