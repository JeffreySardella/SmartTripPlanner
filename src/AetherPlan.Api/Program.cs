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

builder.Services.AddHttpClient<IOllamaClient, OllamaClient>((httpClient, sp) =>
{
    httpClient.BaseAddress = new Uri(builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434");
    httpClient.Timeout = TimeSpan.FromMinutes(5);
    var model = builder.Configuration["Ollama:Model"] ?? "qwen3.5:35b-a3b-q4_K_M";
    return new OllamaClient(httpClient, model);
});

builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddScoped<IPersistenceService, PersistenceService>();

builder.Services.AddControllers();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
