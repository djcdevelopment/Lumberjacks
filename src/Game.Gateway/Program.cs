using Game.Contracts.Protocol;
using Game.Persistence;
using Game.ServiceDefaults;
using Game.Gateway.Valheim;
using Game.Gateway.WebSocket;
using Game.Simulation.Tick;
using Game.Simulation.World;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Gateway services
builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton<MessageRouter>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ValheimPriorityManifestService>();
builder.Services.AddSingleton<ValheimZdoRedirectService>();
builder.Services.AddSingleton<ValheimZdoInjectionService>();
builder.Services.AddSingleton<ValheimHandshakeService>();

// Simulation services (in-process — eliminates HTTP-per-move hop)
builder.Services.AddSingleton<WorldState>();
builder.Services.AddSingleton<InputQueue>();
// Tick timing: OTel-compatible histograms + windowed p50/p99/max log line and /tick snapshot
builder.Services.AddMetrics();
builder.Services.AddSingleton<TickMetrics>();
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
builder.Services.AddScoped<Game.Simulation.Startup.RegionProfileLoader>();
builder.Services.AddScoped<Game.Simulation.Startup.NaturalResourceLoader>();
builder.Services.AddDbContextFactory<GameDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("GameDb")
        ?? "Host=localhost;Port=5433;Database=game;Username=game;Password=game"));

var app = builder.Build();

// Load persisted data into WorldState on startup (graceful — works without DB)
try
{
    await using var scope = app.Services.CreateAsyncScope();
    var regionLoader = scope.ServiceProvider.GetRequiredService<Game.Simulation.Startup.RegionLoader>();
    await regionLoader.LoadAsync();

    var profileLoader = scope.ServiceProvider.GetRequiredService<Game.Simulation.Startup.RegionProfileLoader>();
    await profileLoader.LoadAsync();

    var resourceLoader = scope.ServiceProvider.GetRequiredService<Game.Simulation.Startup.NaturalResourceLoader>();
    await resourceLoader.LoadAsync();

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
Game.Gateway.Endpoints.LiveMetricsEndpoints.Map(app);
// Public Telemetry API v0 (community-telemetry-strategy.md Phase 3) + Live Community View (Phase 4)
Game.Simulation.Endpoints.TelemetryV0Endpoints.Map(app);
Game.Gateway.Endpoints.TelemetryV0SessionsEndpoints.Map(app);
Game.Gateway.Endpoints.CommunityViewEndpoints.Map(app);
ValheimPriorityManifestEndpoints.Map(app);
ValheimZdoRedirectEndpoints.Map(app);
ValheimZdoInjectionEndpoints.Map(app);
ValheimHandshakeEndpoints.Map(app);

app.Run();
