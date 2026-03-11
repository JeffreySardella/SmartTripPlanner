using AetherPlan.Api.Components;
using AetherPlan.Api.Data;
using AetherPlan.Api.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.AddDbContext<AetherPlanDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=AetherPlan.db"));

builder.Services.AddSingleton<ITravelService, TravelService>();

var credentialPath = Path.Combine(builder.Environment.ContentRootPath,
    builder.Configuration["GoogleCalendar:CredentialPath"] ?? "client_secret.json");
var tokenDirectory = Path.Combine(builder.Environment.ContentRootPath,
    builder.Configuration["GoogleCalendar:TokenDirectory"] ?? ".tokens");

var calendarApi = GoogleCalendarFactory.CreateAsync(credentialPath, tokenDirectory)
    .GetAwaiter().GetResult();

if (calendarApi is not null)
{
    Log.Information("Google Calendar API configured successfully");
    builder.Services.AddSingleton(calendarApi);
}
else
{
    Log.Warning("Google Calendar not configured — client_secret.json not found at {Path}. Calendar features disabled.", credentialPath);
}

builder.Services.AddScoped<ICalendarService>(sp =>
{
    var api = sp.GetService<Google.Apis.Calendar.v3.CalendarService>();
    var logger = sp.GetRequiredService<ILogger<CalendarService>>();
    return new CalendarService(api, logger);
});

var llmProvider = builder.Configuration["Llm:Provider"]?.ToLowerInvariant() ?? "ollama";

if (llmProvider == "claude")
{
    var claudeApiKey = builder.Configuration["Claude:ApiKey"]
        ?? Environment.GetEnvironmentVariable("CLAUDE_API_KEY");

    if (string.IsNullOrEmpty(claudeApiKey))
    {
        Log.Error("Claude provider selected but no API key found. Set Claude:ApiKey via dotnet user-secrets or CLAUDE_API_KEY environment variable.");
        throw new InvalidOperationException("Claude API key not configured.");
    }

    var claudeModel = builder.Configuration["Llm:Claude:Model"] ?? "claude-sonnet-4-6";
    Log.Information("LLM Provider: Claude ({Model})", claudeModel);

    builder.Services.AddHttpClient<ILlmClient, ClaudeClient>((httpClient, sp) =>
    {
        httpClient.BaseAddress = new Uri("https://api.anthropic.com");
        httpClient.Timeout = TimeSpan.FromMinutes(5);
        return new ClaudeClient(httpClient, claudeModel, claudeApiKey);
    });
}
else
{
    var ollamaBaseUrl = builder.Configuration["Llm:Ollama:BaseUrl"] ?? "http://localhost:11434";
    var ollamaModel = builder.Configuration["Llm:Ollama:Model"] ?? "qwen3.5:35b-a3b-q4_K_M";
    Log.Information("LLM Provider: Ollama ({Model}) at {BaseUrl}", ollamaModel, ollamaBaseUrl);

    builder.Services.AddHttpClient<ILlmClient, OllamaClient>((httpClient, sp) =>
    {
        httpClient.BaseAddress = new Uri(ollamaBaseUrl);
        httpClient.Timeout = TimeSpan.FromMinutes(5);
        return new OllamaClient(httpClient, ollamaModel);
    });
}

builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddScoped<IPersistenceService, PersistenceService>();
builder.Services.AddScoped<ILocationService, LocationService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("ExtensionPolicy", policy =>
        policy.SetIsOriginAllowed(origin => origin.StartsWith("chrome-extension://"))
              .WithMethods("GET", "POST")
              .AllowAnyHeader());
});

builder.Services.AddControllers();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.UseCors("ExtensionPolicy");

app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
