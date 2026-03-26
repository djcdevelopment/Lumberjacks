using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Game.Gateway.WebSocket;

public record GameSession(string SessionId, string PlayerId, System.Net.WebSockets.WebSocket Socket)
{
    public string? GuildId { get; set; }
}

public class SessionManager
{
    private readonly ConcurrentDictionary<string, GameSession> _sessions = new();

    public GameSession Create(System.Net.WebSockets.WebSocket socket)
    {
        var session = new GameSession(
            SessionId: Guid.NewGuid().ToString(),
            PlayerId: Guid.NewGuid().ToString(),
            Socket: socket);

        _sessions[session.SessionId] = session;
        return session;
    }

    public void Remove(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }

    public IReadOnlyCollection<GameSession> GetAll() => _sessions.Values.ToList();
}
