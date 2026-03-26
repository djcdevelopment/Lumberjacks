namespace Game.Contracts.Entities;

public record Player
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? GuildId { get; init; }
    public int Rank { get; init; }
    public required Vec3 Position { get; init; }
    public required string RegionId { get; init; }
    public bool Connected { get; init; }
    public DateTimeOffset? ConnectedAt { get; init; }
}
