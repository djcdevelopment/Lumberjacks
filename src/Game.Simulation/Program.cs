using Game.Persistence;
using Game.ServiceDefaults;
using Game.Simulation.Handlers;
using Game.Simulation.Tick;
using Game.Simulation.World;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddSingleton<WorldState>();
builder.Services.AddHostedService<TickLoop>();
builder.Services.AddScoped<PlaceStructureHandler>();
builder.Services.AddScoped<InventoryHandler>();
builder.Services.AddScoped<Game.Simulation.Startup.StructureLoader>();
builder.Services.AddDbContextFactory<GameDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("GameDb")
        ?? "Host=localhost;Database=game;Username=game;Password=game"));

var app = builder.Build();

// Load persisted structures into WorldState on startup
await using (var scope = app.Services.CreateAsyncScope())
{
    var loader = scope.ServiceProvider.GetRequiredService<Game.Simulation.Startup.StructureLoader>();
    await loader.LoadAsync();
}

app.MapServiceDefaults();

Game.Simulation.Endpoints.RegionEndpoints.Map(app);
Game.Simulation.Endpoints.PlayerEndpoints.Map(app);
Game.Simulation.Endpoints.StructureEndpoints.Map(app);
Game.Simulation.Endpoints.InventoryEndpoints.Map(app);

app.Run();
