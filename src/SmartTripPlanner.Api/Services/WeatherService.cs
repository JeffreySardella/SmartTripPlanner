namespace SmartTripPlanner.Api.Services;

using System.Text.Json;

public class WeatherService(IHttpClientFactory httpClientFactory, ILogger<WeatherService> logger)
{
    public async Task<object> GetWeatherAsync(double latitude, double longitude, DateTime date)
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var dateStr = date.ToString("yyyy-MM-dd");
            var endDate = date.AddDays(6).ToString("yyyy-MM-dd");

            var url = $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}" +
                      $"&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max,weathercode" +
                      $"&start_date={dateStr}&end_date={endDate}&timezone=auto";

            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            if (!json.TryGetProperty("daily", out var daily))
                return new { error = "No forecast data available" };

            var dates = daily.GetProperty("time").EnumerateArray().Select(d => d.GetString()).ToList();
            var maxTemps = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(t => t.GetDouble()).ToList();
            var minTemps = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(t => t.GetDouble()).ToList();
            var rainChance = daily.GetProperty("precipitation_probability_max").EnumerateArray().Select(r => r.GetInt32()).ToList();
            var codes = daily.GetProperty("weathercode").EnumerateArray().Select(c => c.GetInt32()).ToList();

            var forecast = dates.Select((d, i) => new
            {
                date = d,
                high_c = maxTemps[i],
                high_f = Math.Round(maxTemps[i] * 9.0 / 5 + 32, 1),
                low_c = minTemps[i],
                low_f = Math.Round(minTemps[i] * 9.0 / 5 + 32, 1),
                rain_percent = rainChance[i],
                condition = WeatherCodeToDescription(codes[i])
            }).ToList();

            return new { latitude, longitude, forecast };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Weather API failed for {Lat},{Lon}", latitude, longitude);
            return new { error = $"Weather unavailable: {ex.Message}" };
        }
    }

    private static string WeatherCodeToDescription(int code) => code switch
    {
        0 => "Clear sky",
        1 or 2 or 3 => "Partly cloudy",
        45 or 48 => "Foggy",
        51 or 53 or 55 => "Drizzle",
        61 or 63 or 65 => "Rain",
        66 or 67 => "Freezing rain",
        71 or 73 or 75 => "Snow",
        80 or 81 or 82 => "Rain showers",
        85 or 86 => "Snow showers",
        95 => "Thunderstorm",
        96 or 99 => "Thunderstorm with hail",
        _ => "Unknown"
    };
}
