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

builder.Services.AddHttpClient<IOllamaClient, OllamaClient>((httpClient, sp) =>
{
    httpClient.BaseAddress = new Uri(builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434");
    httpClient.Timeout = TimeSpan.FromMinutes(5);
    var model = builder.Configuration["Ollama:Model"] ?? "qwen3.5:35b-a3b-q4_K_M";
    return new OllamaClient(httpClient, model);
});

builder.Services.AddScoped<IAgentService, AgentService>();

builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
