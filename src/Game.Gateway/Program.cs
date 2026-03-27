using Game.Contracts.Protocol;
using Game.Persistence;
using Game.ServiceDefaults;
using Game.Gateway.WebSocket;
using Game.Simulation.Tick;
using Game.Simulation.World;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Gateway services
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<MessageRouter>();

// Simulation services (in-process — eliminates HTTP-per-move hop)
builder.Services.AddSingleton<WorldState>();
builder.Services.AddSingleton<InputQueue>();
builder.Services.AddSingleton<UdpTransport>();
builder.Services.AddSingleton<TickBroadcaster>();
builder.Services.AddSingleton<ITickBroadcaster>(sp => sp.GetRequiredService<TickBroadcaster>());
builder.Services.AddHostedService<TickLoop>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<UdpTransport>());
// Singletons: handlers use IDbContextFactory (not DbContext) so they're stateless.
// Must be singleton because MessageRouter (also singleton) depends on them directly.
builder.Services.AddSingleton<Game.Simulation.Handlers.PlayerHandler>();
builder.Services.AddSingleton<Game.Simulation.Handlers.PlaceStructureHandler>();
builder.Services.AddSingleton<Game.Simulation.Handlers.InventoryHandler>();
builder.Services.AddScoped<Game.Simulation.Startup.StructureLoader>();
builder.Services.AddScoped<Game.Simulation.Startup.RegionLoader>();
builder.Services.AddDbContextFactory<GameDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("GameDb")
        ?? "Host=localhost;Database=game;Username=game;Password=game"));

var app = builder.Build();

// Load persisted data into WorldState on startup (graceful — works without DB)
try
{
    await using var scope = app.Services.CreateAsyncScope();
    var regionLoader = scope.ServiceProvider.GetRequiredService<Game.Simulation.Startup.RegionLoader>();
    await regionLoader.LoadAsync();

    var structureLoader = scope.ServiceProvider.GetRequiredService<Game.Simulation.Startup.StructureLoader>();
    await structureLoader.LoadAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Could not load persisted data — running with in-memory defaults only");
}

app.MapServiceDefaults();
app.UseWebSockets();
app.UseMiddleware<GameWebSocketMiddleware>();

// Expose simulation HTTP endpoints (for admin/debugging and legacy clients)
Game.Simulation.Endpoints.RegionEndpoints.Map(app);
Game.Simulation.Endpoints.PlayerEndpoints.Map(app);
Game.Simulation.Endpoints.StructureEndpoints.Map(app);
Game.Simulation.Endpoints.InventoryEndpoints.Map(app);
Game.Simulation.Endpoints.TickEndpoints.Map(app);

app.Run();
