namespace Game.Contracts.Entities;

public record Session
{
    public required string SessionId { get; init; }
    public required string PlayerId { get; init; }
    public required string WorldId { get; init; }
    public required string RegionId { get; init; }
    public required DateTimeOffset ConnectedAt { get; init; }
    public required string Token { get; init; }
}
