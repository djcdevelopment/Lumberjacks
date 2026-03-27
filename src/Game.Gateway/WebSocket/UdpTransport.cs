using System.Net;
using System.Net.Sockets;
using Game.Contracts.Protocol.Binary;

namespace Game.Gateway.WebSocket;

/// <summary>
/// UDP datagram channel — runs alongside the WebSocket server.
/// Handles the Datagram delivery lane (player_input inbound, entity_update outbound).
///
/// Packet format (both directions):
///   [udpToken: 8 bytes] [binaryEnvelope: header + payload]
///
/// Flow:
///   1. Client connects via WebSocket, receives session_started with udp_token + udp_port
///   2. Client sends first UDP packet to udp_port — server maps the source endpoint to the session
///   3. Server sends datagram-lane messages to the bound UDP endpoint
///   4. If no UDP endpoint is bound, TickBroadcaster falls back to WebSocket
/// </summary>
public class UdpTransport : BackgroundService
{
    public const int TokenBytes = 8;
    public const int MinPacketSize = TokenBytes + BinaryEnvelope.HeaderBytes;

    private readonly SessionManager _sessions;
    private readonly MessageRouter _router;
    private readonly ILogger<UdpTransport> _logger;
    private readonly int _port;
    private UdpClient? _client;

    public int Port => _port;

    public UdpTransport(
        SessionManager sessions,
        MessageRouter router,
        IConfiguration config,
        ILogger<UdpTransport> logger)
    {
        _sessions = sessions;
        _router = router;
        _logger = logger;
        _port = config.GetValue("Udp:Port", 4005);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _client = new UdpClient(_port);
        _logger.LogInformation("UDP transport listening on port {Port}", _port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await _client.ReceiveAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (result.Buffer.Length < MinPacketSize)
                    continue; // too small to be valid

                try
                {
                    ProcessPacket(result.Buffer, result.RemoteEndPoint);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to process UDP packet from {Endpoint}", result.RemoteEndPoint);
                }
            }
        }
        finally
        {
            _client.Dispose();
            _logger.LogInformation("UDP transport stopped");
        }
    }

    private void ProcessPacket(byte[] data, IPEndPoint remoteEndpoint)
    {
        // Extract 8-byte UDP token
        var token = BitConverter.ToUInt64(data, 0);

        var session = _sessions.FindByUdpToken(token);
        if (session == null)
            return; // unknown token — drop silently

        // Bind/update the UDP endpoint for this session
        session.UdpEndpoint = remoteEndpoint;

        // Parse binary envelope from the rest of the packet
        var envelope = data.AsSpan(TokenBytes);
        if (envelope.Length < BinaryEnvelope.HeaderBytes)
            return;

        var header = BinaryEnvelope.ReadHeader(envelope);

        // Fast path: player_input
        if (header.Type == MessageTypeId.PlayerInput)
        {
            var payload = BinaryEnvelope.GetPayload(envelope, header);
            var input = PayloadSerializers.ReadPlayerInput(payload);
            _router.HandlePlayerInputBinary(session, input);
            return;
        }

        // Other datagram-lane messages could be handled here in the future
        _logger.LogDebug("Unhandled UDP message type {Type} from {PlayerId}", header.Type, session.PlayerId);
    }

    /// <summary>
    /// Send a raw datagram to a session's bound UDP endpoint.
    /// Returns false if the session has no UDP endpoint or send fails.
    /// </summary>
    public bool TrySend(GameSession session, ReadOnlySpan<byte> binaryFrame)
    {
        if (_client == null || session.UdpEndpoint == null)
            return false;

        try
        {
            // Prepend UDP token to the binary frame
            var packet = new byte[TokenBytes + binaryFrame.Length];
            BitConverter.TryWriteBytes(packet.AsSpan(0, TokenBytes), session.UdpToken);
            binaryFrame.CopyTo(packet.AsSpan(TokenBytes));

            _client.Send(packet, packet.Length, session.UdpEndpoint);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
