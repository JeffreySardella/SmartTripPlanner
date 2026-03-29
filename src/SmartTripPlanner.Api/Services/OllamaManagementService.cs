namespace SmartTripPlanner.Api.Services;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

public class OllamaManagementService(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<OllamaManagementService> logger)
{
    private string BaseUrl => config["Llm:Ollama:BaseUrl"] ?? "http://localhost:11434";

    public async Task<List<OllamaModel>> ListModelsAsync()
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync($"{BaseUrl}/api/tags");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>();
            return json?.Models ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to list Ollama models");
            return [];
        }
    }

    public async Task<OllamaStatus> GetStatusAsync()
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            var response = await client.GetAsync($"{BaseUrl}/api/tags");

            return new OllamaStatus
            {
                IsRunning = response.IsSuccessStatusCode,
                Endpoint = BaseUrl
            };
        }
        catch
        {
            return new OllamaStatus { IsRunning = false, Endpoint = BaseUrl };
        }
    }

    public async Task<OllamaModelDetail?> GetModelInfoAsync(string modelName)
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.PostAsJsonAsync($"{BaseUrl}/api/show", new { name = modelName });
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var detail = new OllamaModelDetail { Name = modelName };

            if (json.TryGetProperty("details", out var details))
            {
                detail.Family = details.TryGetProperty("family", out var f) ? f.GetString() : null;
                detail.ParameterSize = details.TryGetProperty("parameter_size", out var p) ? p.GetString() : null;
                detail.QuantizationLevel = details.TryGetProperty("quantization_level", out var q) ? q.GetString() : null;
            }

            return detail;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get model info for {Model}", modelName);
            return null;
        }
    }

    public async Task<BenchmarkResult> BenchmarkModelAsync(string modelName, string testPrompt)
    {
        var result = new BenchmarkResult { Model = modelName };

        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(5);

            var request = new
            {
                model = modelName,
                messages = new[] { new { role = "user", content = testPrompt } },
                stream = false
            };

            var sw = Stopwatch.StartNew();
            var response = await client.PostAsJsonAsync($"{BaseUrl}/api/chat", request);
            sw.Stop();

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            result.TimeSec = Math.Round(sw.Elapsed.TotalSeconds, 1);
            result.Success = true;

            if (json.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
            {
                var text = content.GetString() ?? "";
                result.ResponseLength = text.Length;
            }

            // Extract token stats if available
            if (json.TryGetProperty("eval_count", out var evalCount))
                result.TokenCount = evalCount.GetInt32();
            if (json.TryGetProperty("eval_duration", out var evalDur) && result.TokenCount > 0)
            {
                var durationNs = evalDur.GetDouble();
                result.TokensPerSec = Math.Round(result.TokenCount / (durationNs / 1_000_000_000.0), 1);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            logger.LogWarning(ex, "Benchmark failed for {Model}", modelName);
        }

        return result;
    }

    public async Task<List<GpuInfo>> GetAllGpusAsync()
    {
        var gpus = new List<GpuInfo>();

        // Detect GPUs via PowerShell (works on Windows with AMD/NVIDIA/Intel)
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-Command \"Get-CimInstance Win32_VideoController | Select-Object Name, AdapterRAM | ConvertTo-Json\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();

                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(output);
                    var items = json.ValueKind == JsonValueKind.Array ? json.EnumerateArray() : new[] { json }.AsEnumerable();

                    foreach (var gpu in items)
                    {
                        var name = gpu.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                        var adapterRam = gpu.TryGetProperty("AdapterRAM", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt64() : 0;

                        // Skip virtual adapters and tiny integrated GPUs
                        if (name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || adapterRam == 0)
                            continue;

                        var backend = name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? "ROCm"
                            : name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "CUDA"
                            : "Integrated";

                        // WMI caps AdapterRAM at 4GB for large GPUs — use known values
                        var totalMb = name switch
                        {
                            _ when name.Contains("7900 XTX") => 24_576,
                            _ when name.Contains("7900 XT") && !name.Contains("XTX") => 20_480,
                            _ when name.Contains("6800 XT") => 16_384,
                            _ when name.Contains("6800") => 16_384,
                            _ => (int)(adapterRam / 1_048_576)
                        };

                        gpus.Add(new GpuInfo
                        {
                            Name = name,
                            VramTotalMb = totalMb,
                            VramUsedMb = 0,
                            Backend = backend
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to detect GPUs via PowerShell");
        }

        // Get VRAM usage from Ollama for the active GPU
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var response = await client.GetAsync($"{BaseUrl}/api/ps");

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                long totalVramUsed = 0;

                if (json.TryGetProperty("models", out var models))
                {
                    foreach (var model in models.EnumerateArray())
                    {
                        if (model.TryGetProperty("size_vram", out var sizeVram))
                            totalVramUsed += sizeVram.GetInt64();
                    }
                }

                // Apply VRAM usage to the biggest discrete GPU (Ollama's target)
                var primary = gpus.OrderByDescending(g => g.VramTotalMb).FirstOrDefault();
                if (primary is not null)
                    primary.VramUsedMb = Math.Min((int)(totalVramUsed / 1_048_576), primary.VramTotalMb);
            }
        }
        catch { /* Ollama might not be running */ }

        return gpus;
    }

    public async Task<GpuInfo?> GetGpuInfoAsync()
    {
        var gpus = await GetAllGpusAsync();
        return gpus.OrderByDescending(g => g.VramTotalMb).FirstOrDefault();
    }
}

public class OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public List<OllamaModel> Models { get; set; } = [];
}

public class OllamaModel
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("details")]
    public OllamaModelDetails? Details { get; set; }
}

public class OllamaModelDetails
{
    [JsonPropertyName("parameter_size")]
    public string? ParameterSize { get; set; }

    [JsonPropertyName("quantization_level")]
    public string? QuantizationLevel { get; set; }

    [JsonPropertyName("family")]
    public string? Family { get; set; }
}

public class OllamaModelDetail
{
    public string Name { get; set; } = "";
    public string? Family { get; set; }
    public string? ParameterSize { get; set; }
    public string? QuantizationLevel { get; set; }
}

public class OllamaStatus
{
    public bool IsRunning { get; set; }
    public string Endpoint { get; set; } = "";
}

public class BenchmarkResult
{
    public string Model { get; set; } = "";
    public bool Success { get; set; }
    public double TimeSec { get; set; }
    public int ResponseLength { get; set; }
    public int TokenCount { get; set; }
    public double TokensPerSec { get; set; }
    public string? Error { get; set; }
}

public class GpuInfo
{
    public string Name { get; set; } = "";
    public int VramUsedMb { get; set; }
    public int VramTotalMb { get; set; }
    public string Backend { get; set; } = "";
    public int VramFreeMb => VramTotalMb - VramUsedMb;
    public int VramUsagePercent => VramTotalMb > 0 ? (int)(VramUsedMb * 100.0 / VramTotalMb) : 0;
}
