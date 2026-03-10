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

builder.Services.AddControllers();

var app = builder.Build();
app.MapControllers();
app.Run();
