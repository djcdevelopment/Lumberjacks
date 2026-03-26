using Game.Simulation.World;

namespace Game.Simulation.Tick;

public class TickLoop : BackgroundService
{
    private readonly WorldState _world;
    private readonly ILogger<TickLoop> _logger;
    private const int TickMs = 50; // 20 Hz

    public TickLoop(WorldState world, ILogger<TickLoop> logger)
    {
        _world = world;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Tick loop started at {TickRate}Hz", 1000 / TickMs);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickMs));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            // TODO: Process movement, resolve placements, emit events,
            // interest management, activation tiers, authoritative state resolution
        }

        _logger.LogInformation("Tick loop stopped");
    }
}
