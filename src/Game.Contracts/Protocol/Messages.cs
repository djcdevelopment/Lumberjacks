using Game.Contracts.Entities;

namespace Game.Contracts.Protocol;

// Client → Server messages
public record JoinRegionMessage(string RegionId, string Token);
public record LeaveRegionMessage(string RegionId);
public record PlayerMoveMessage(Vec3 Position, Vec3 Velocity, long Timestamp);
public record PlaceStructureMessage(string StructureType, Vec3 Position, double Rotation);

// Server → Client messages
public record SessionStartedMessage(string SessionId, string PlayerId, string WorldId);
public record WorldSnapshotMessage(string RegionId, List<Dictionary<string, object>> Entities, int Tick);
public record EntityUpdateMessage(string EntityId, string EntityType, Dictionary<string, object> Data, int Tick);
public record EntityRemovedMessage(string EntityId, int Tick);
public record ErrorMessage(string Code, string Message);
