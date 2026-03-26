using System.Net.WebSockets;
using System.Text;
using Game.Contracts.Protocol;

namespace Game.Gateway.WebSocket;

public class GameWebSocketMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SessionManager _sessions;
    private readonly ILogger<GameWebSocketMiddleware> _logger;
    private readonly IServiceProvider _services;

    public GameWebSocketMiddleware(RequestDelegate next, SessionManager sessions, ILogger<GameWebSocketMiddleware> logger, IServiceProvider services)
    {
        _next = next;
        _sessions = sessions;
        _logger = logger;
        _services = services;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            await _next(context);
            return;
        }

        var ws = await context.WebSockets.AcceptWebSocketAsync();
        var session = _sessions.Create(ws);
        _logger.LogInformation("Session {SessionId} connected (player {PlayerId})", session.SessionId, session.PlayerId);

        // Send session_started envelope
        var startedMsg = new SessionStartedMessage(session.SessionId, session.PlayerId, "world-default");
        var envelope = EnvelopeFactory.Create(MessageType.SessionStarted, startedMsg);
        var json = EnvelopeFactory.Serialize(envelope);
        await ws.SendAsync(
            Encoding.UTF8.GetBytes(json),
            WebSocketMessageType.Text,
            true,
            CancellationToken.None);

        // Get the message router
        var router = _services.GetRequiredService<MessageRouter>();

        // Receive loop
        var buffer = new byte[4096];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                var raw = Encoding.UTF8.GetString(buffer, 0, result.Count);
                try
                {
                    var incoming = EnvelopeFactory.Parse(raw);
                    _logger.LogDebug("Session {SessionId} received {Type}", session.SessionId, incoming.Type);

                    // Route to appropriate service
                    await router.RouteAsync(session, incoming);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process message from {SessionId}", session.SessionId);
                    var error = new ErrorMessage("INVALID_MESSAGE", "Failed to parse message");
                    var errEnvelope = EnvelopeFactory.Create(MessageType.Error, error);
                    var errJson = EnvelopeFactory.Serialize(errEnvelope);
                    await ws.SendAsync(
                        Encoding.UTF8.GetBytes(errJson),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                }
            }
        }
        finally
        {
            // Clean up player from simulation before removing session
            try { await router.HandleDisconnectAsync(session); } catch { }

            _sessions.Remove(session.SessionId);
            _logger.LogInformation("Session {SessionId} disconnected", session.SessionId);

            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Goodbye", CancellationToken.None);
        }
    }
}
