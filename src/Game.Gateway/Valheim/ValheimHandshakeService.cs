using System.Collections.Concurrent;

namespace Game.Gateway.Valheim;

/// <summary>
/// Valheim connection-status codes, mirrored from the decompiled <c>ZNet.ConnectionStatus</c>
/// enum (assembly_valheim.dll 0.221.12, ZNet.decompiled.cs:23-38). The handshake gate emits
/// these as the "Error" RPC argument. See fieldlab/NETCODE-HANDSHAKE-CONTRACT.md.
/// </summary>
public enum ValheimConnectionStatus
{
    None = 0,
    Connecting = 1,
    Connected = 2,
    ErrorVersion = 3,
    ErrorDisconnected = 4,
    ErrorConnectFailed = 5,
    ErrorPassword = 6,
    ErrorAlreadyConnected = 7,
    ErrorBanned = 8,
    ErrorFull = 9,
    ErrorPlatformExcluded = 10,
    ErrorCrossplayPrivilege = 11,
    ErrorKicked = 12,
}

/// <summary>
/// The emulated dedicated-server context a Lumberjacks-fronted peer answers a handshake
/// against — the logical inputs to Funnel 5's gate. Defaults mirror the am4 ComfyEra16
/// Steam-only server (no password, 0/10 players, no ban/whitelist). The mod hook (P6 step 5)
/// supplies the real values at connect time; the loopback shim overrides per battery case.
/// </summary>
public sealed record ValheimHandshakeServerContext
{
    public int NetworkVersion { get; init; } = ValheimHandshakeService.NetworkVersion;

    /// <summary>Comparison target for the password gate — equals the server's stored
    /// <c>m_serverPassword</c> (an opaque hash string). Empty ⇒ needPassword=false.</summary>
    public string Password { get; init; } = string.Empty;

    public string Salt { get; init; } = string.Empty;
    public int MaxPlayers { get; init; } = ValheimHandshakeService.MaxPlayers;
    public int CurrentPlayers { get; init; }
    public bool RequireSteamTicket { get; init; } = true;
    public IReadOnlyList<string> BannedHosts { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PermittedHosts { get; init; } = Array.Empty<string>();
    public IReadOnlyList<long> ConnectedUids { get; init; } = Array.Empty<long>();

    /// <summary>
    /// Lumberjacks seat gate: concurrent seats this window admits. The volunteer pilot is one seat
    /// (VOLUNTEER-ENDPOINT.md: "Admit only one client while P7 still uses the shared p7-primary-v1
    /// queue"), so 1 is the default — including for a window that materializes on first contact
    /// without an operator ever configuring it. 0 disables the gate.
    ///
    /// This is NOT the native <see cref="MaxPlayers"/> check: that one emulates vanilla's
    /// <c>GetNrOfPlayers() >= 10</c> against an operator-supplied <see cref="CurrentPlayers"/> that
    /// nothing keeps current. The seat gate counts what the Gateway itself admitted.
    /// </summary>
    public int SeatCapacity { get; init; } = 1;

    /// <summary>
    /// How long a seat survives with no sign of life from its holder — see
    /// <see cref="ValheimWindowActivityService"/> for why liveness, not a plain timer, drives this.
    ///
    /// 60s is chosen against the failure that actually bites: the sole volunteer crashing and
    /// wanting back in (plan §5.4). Their session uid is regenerated per game process, so a rejoin
    /// cannot be recognised as the same player and must wait out the lease. Relaunching Valheim
    /// takes longer than 60s, so in practice the seat is already free when they return. The cost of
    /// the short lease is that a holder whose consumer stalls for a full minute could have their
    /// seat taken — which requires a second player to exist, and today there is one volunteer.
    /// </summary>
    public int SeatLeaseSeconds { get; init; } = 60;

    // Server-PeerInfo reply fields (the emulated world).
    public long ServerUid { get; init; } = 1;
    public string VersionString { get; init; } = "0.221.12";
    public string WorldName { get; init; } = "ComfyEra16";
    public int Seed { get; init; }
    public string SeedName { get; init; } = string.Empty;
    public long WorldUid { get; init; }
    public int WorldGenVersion { get; init; }
    public double NetTime { get; init; }
}

public sealed record ValheimHandshakeConfigRequest
{
    public string? WindowId { get; init; }
    public ValheimHandshakeServerContext? Context { get; init; }
}

/// <summary>Response to the client's ServerHandshake — the ClientHandshake args.</summary>
public sealed record ValheimHandshakeBeginResult(bool NeedPassword, string Salt, int NetworkVersion);

/// <summary>
/// The decoded client PeerInfo, as the mod hook would hand it over after reading the
/// ZPackage. Logical fields only — the byte layout stays in the mod.
/// </summary>
public sealed record ValheimPeerInfoSubmission
{
    public string? WindowId { get; init; }
    public string? ConnectionId { get; init; }
    public long Uid { get; init; }
    public string? Version { get; init; }
    public int NetVersion { get; init; }
    public double[]? RefPos { get; init; }
    public string? PlayerName { get; init; }
    public string? HostName { get; init; }
    public string? PasswordHash { get; init; }
    /// <summary>The mod's Steamworks VerifySessionTicket result (gate check C). The real
    /// crypto verify can only run in-game, so it is supplied as a boolean.</summary>
    public bool TicketValid { get; init; } = true;
}

public sealed record ValheimServerPeerInfo(
    long Uid, string Version, int NetVersion, string WorldName, int Seed,
    string SeedName, long WorldUid, int WorldGenVersion, double NetTime);

/// <summary>
/// Result of the logical handshake gate. <see cref="EntersSteadyState"/> means the gate
/// accepted and WOULD drive Valheim's AddPeer transition — it is a headless decision, NOT an
/// observation of a real ZDOMan.AddPeer / in-world peer. The live proof is the P6 step 6-7
/// gate (Derek connects in-game, ≥30s in-world).
/// </summary>
public sealed record ValheimHandshakeGateResult(
    bool Accept,
    bool EntersSteadyState,
    int? ErrorCode,
    string? ErrorName,
    string? FailedCheck,
    ValheimServerPeerInfo? ServerPeerInfo);

public sealed record ValheimHandshakeExchangeRecord(
    string ConnectionId,
    bool Accept,
    bool EnteredSteadyState,
    int? ErrorCode,
    string? ErrorName,
    string? FailedCheck,
    DateTime Utc);

public sealed record ValheimHandshakeWindowStatus(
    string WindowId,
    int NetworkVersion,
    bool NeedPassword,
    int MaxPlayers,
    int CurrentPlayers,
    bool RequireSteamTicket,
    int BannedHosts,
    int PermittedHosts,
    long Begins,
    long Accepted,
    long Rejected,
    long SteadyStateReached,
    IReadOnlyDictionary<string, long> ByCode,
    IReadOnlyList<ValheimHandshakeExchangeRecord> Exchanges,
    DateTime? FirstUtc,
    DateTime? LastUtc);

/// <summary>
/// Logical handshake responder for the I5 / P6 rung: given the two handshake decision points
/// (ServerHandshake ⇒ ClientHandshake args; PeerInfo ⇒ accept-or-reject), it replicates
/// Funnel 5's ordered gate and error-code contract from a running Valheim dedicated server,
/// WITHOUT parsing raw ZPackage bytes (the mod owns that, as in I3/I4). Windowed and
/// in-memory, matching ValheimZdoRedirectService / ValheimZdoInjectionService. Contract:
/// fieldlab/NETCODE-HANDSHAKE-CONTRACT.md (two decompile sources agree).
/// </summary>
public sealed class ValheimHandshakeService
{
    /// <summary>Literal network-version gate value — <c>Version.m_networkVersion = 36u</c>.</summary>
    public const int NetworkVersion = 36;

    /// <summary>Server-full threshold — the literal <c>>= 10</c> in RPC_PeerInfo:912 (NOT the
    /// unused c_NetworkVersionMaxPlayerCount=35 const, a catalogued red herring).</summary>
    public const int MaxPlayers = 10;

    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly ValheimWindowActivityService? _activity;
    private readonly Func<DateTime> _nowUtc;

    /// <param name="activity">Consumer liveness for the seat gate. Null ⇒ seats expire on their
    /// grant alone, which is the pre-stage-2 behaviour and is what unit tests get by default.</param>
    /// <param name="nowUtc">Injected so seat-lease expiry is testable without sleeping.</param>
    public ValheimHandshakeService(
        ValheimWindowActivityService? activity = null, Func<DateTime>? nowUtc = null)
    {
        _activity = activity;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
    }

    public (bool Ok, string Error) Configure(string windowId, ValheimHandshakeServerContext context)
    {
        var error = ValidateToken(windowId, "window_id") ?? ValidateContext(context);
        if (error is not null)
            return (false, error);
        _windows.GetOrAdd(windowId, static _ => new Window()).Configure(context);
        return (true, string.Empty);
    }

    public (bool Ok, string Error, ValheimHandshakeBeginResult? Result) Begin(
        string windowId, string connectionId)
    {
        var error = ValidateToken(windowId, "window_id") ?? ValidateToken(connectionId, "connection_id");
        if (error is not null)
            return (false, error, null);
        var window = _windows.GetOrAdd(windowId, static _ => new Window());
        return (true, string.Empty, window.Begin(connectionId));
    }

    public (bool Ok, string Error, ValheimHandshakeGateResult? Result) SubmitPeerInfo(
        string windowId, ValheimPeerInfoSubmission submission)
    {
        var error = ValidateToken(windowId, "window_id") ?? ValidateSubmission(submission);
        if (error is not null)
            return (false, error, null);
        var window = _windows.GetOrAdd(windowId, static _ => new Window());
        return (true, string.Empty, window.SubmitPeerInfo(
            submission, _nowUtc(), _activity?.LastActivityUtc(windowId)));
    }

    public ValheimHandshakeWindowStatus GetStatus(string windowId) =>
        _windows.TryGetValue(windowId, out var window)
            ? window.Status(windowId)
            : Window.Empty(windowId);

    public IReadOnlyList<ValheimHandshakeWindowStatus> GetAllStatuses() =>
        _windows.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Value.Status(kv.Key)).ToList();

    public bool Reset(string windowId)
    {
        // Liveness goes with the window: a surviving mark would hold a seat in a window that was
        // just reset to empty.
        _activity?.Clear(windowId);
        return _windows.TryRemove(windowId, out _);
    }

    public int ResetAll()
    {
        var count = _windows.Count;
        _windows.Clear();
        _activity?.ClearAll();
        return count;
    }

    // --- validation -------------------------------------------------------------------

    public static string? ValidateContext(ValheimHandshakeServerContext? context)
    {
        if (context is null)
            return "context is required";
        if (context.NetworkVersion is < 0 or > ushort.MaxValue)
            return "network_version out of range";
        if (context.MaxPlayers is < 1 or > 1000)
            return "max_players must be 1..1000";
        if (context.CurrentPlayers is < 0 or > 100_000)
            return "current_players out of range";
        // Refused rather than clamped: the seat gate's liveness is window-scoped, so it can tell
        // that SOMEONE is consuming but not WHICH holder. That is exactly enough for one seat and
        // incoherent for more — an N-seat window would let a single live consumer vouch for N
        // departed players. Honouring the number would be a silent correctness bug; N seats need
        // per-holder liveness, which needs the stage-3 identity (plan §5.4).
        if (context.SeatCapacity is < 0 or > 1)
            return "seat_capacity must be 0 (disabled) or 1; N-seat windows need per-holder liveness";
        if (context.SeatLeaseSeconds is < 1 or > 86_400)
            return "seat_lease_seconds must be 1..86400";
        return null;
    }

    public static string? ValidateSubmission(ValheimPeerInfoSubmission? s)
    {
        if (s is null)
            return "submission is required";
        var tokenError = ValidateToken(s.ConnectionId, "connection_id");
        if (tokenError is not null)
            return tokenError;
        // The client version string is always written before the network-version uint
        // (SendPeerInfo:787). The mod applies the 0.214.301 conditional-read shim when decoding
        // (a pre-0.214.301 client yields net_version=0, which then fails gate check A) and hands
        // the resulting net_version here; a missing version string is a malformed PeerInfo.
        if (string.IsNullOrWhiteSpace(s.Version) || s.Version.Length > 64)
            return "version string is required (1-64 characters)";
        if (string.IsNullOrWhiteSpace(s.PlayerName) || s.PlayerName.Length > 128)
            return "player_name must be 1-128 characters";
        if (string.IsNullOrWhiteSpace(s.HostName) || s.HostName.Length > 256)
            return "host_name must be 1-256 characters";
        if (s.RefPos is null || s.RefPos.Length != 3 || s.RefPos.Any(v => !double.IsFinite(v)))
            return "ref_pos must contain three finite numbers";
        return null;
    }

    private static string? ValidateToken(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128
            || value.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '.')))
            return $"{name} must use 1-128 letters, digits, '.', '_' or '-'";
        return null;
    }

    private sealed class Window
    {
        // Bound the exchange trace so a runaway sender can't grow it without limit; counters
        // keep accumulating past the cap.
        private const int MaxTrackedExchanges = 10_000;

        private readonly object _gate = new();
        private readonly List<ValheimHandshakeExchangeRecord> _exchanges = new();
        private readonly Dictionary<int, long> _byCode = new();
        private readonly HashSet<long> _connectedUids = new();

        private ValheimHandshakeServerContext _context = new();
        private long _begins;
        private long _accepted;
        private long _rejected;
        private long _steadyState;
        private DateTime? _firstUtc;
        private DateTime? _lastUtc;

        /// <summary>Uids currently holding a seat, and when each was last granted or refreshed.
        /// Bounded by SeatCapacity, so this cannot grow the way _connectedUids does.</summary>
        private readonly Dictionary<long, DateTime> _seats = new();

        public void Configure(ValheimHandshakeServerContext context)
        {
            lock (_gate)
            {
                _context = context;
                _connectedUids.Clear();
                foreach (var uid in context.ConnectedUids)
                    _connectedUids.Add(uid);
                // Reconfiguring re-states the world; a seat held under the old context would be a
                // reservation for a server that no longer exists.
                _seats.Clear();
            }
        }

        public ValheimHandshakeBeginResult Begin(string connectionId)
        {
            lock (_gate)
            {
                _begins++;
                Touch();
                var needPassword = !string.IsNullOrEmpty(_context.Password);
                return new ValheimHandshakeBeginResult(needPassword, _context.Salt, _context.NetworkVersion);
            }
        }

        public ValheimHandshakeGateResult SubmitPeerInfo(
            ValheimPeerInfoSubmission s, DateTime nowUtc, DateTime? lastActivityUtc)
        {
            lock (_gate)
            {
                var result = Evaluate(s, nowUtc, lastActivityUtc);
                Touch();

                if (result.Accept)
                {
                    _accepted++;
                    if (result.EntersSteadyState)
                    {
                        _steadyState++;
                        _connectedUids.Add(s.Uid); // now IsConnected(uid) for the next attempt
                        TakeSeat(s.Uid, nowUtc);
                    }
                }
                else
                {
                    _rejected++;
                    if (result.ErrorCode is int code)
                        _byCode[code] = _byCode.GetValueOrDefault(code) + 1;
                }

                if (_exchanges.Count < MaxTrackedExchanges)
                {
                    _exchanges.Add(new ValheimHandshakeExchangeRecord(
                        s.ConnectionId!, result.Accept, result.EntersSteadyState,
                        result.ErrorCode, result.ErrorName, result.FailedCheck, DateTime.UtcNow));
                }

                return result;
            }
        }

        /// <summary>The ordered gate — the checks run in the exact decompile order; the first
        /// failure returns its code. Mirrors RPC_PeerInfo:835-926. The Lumberjacks seat gate (H)
        /// runs only after all six native checks, so a client vanilla would have rejected still
        /// gets vanilla's own code and label rather than a Lumberjacks one.</summary>
        private ValheimHandshakeGateResult Evaluate(
            ValheimPeerInfoSubmission s, DateTime nowUtc, DateTime? lastActivityUtc)
        {
            // A — network version (RPC_PeerInfo:835)
            if (s.NetVersion != _context.NetworkVersion)
                return Reject(ValheimConnectionStatus.ErrorVersion, "version");

            // B — blacklist / whitelist (IsAllowed, :871 / :2512)
            if (!IsAllowed(s.HostName ?? string.Empty, s.PlayerName ?? string.Empty))
                return Reject(ValheimConnectionStatus.ErrorBanned, "blacklist");

            // C — Steam session ticket (:878-888; verify result supplied by the mod)
            if (_context.RequireSteamTicket && !s.TicketValid)
                return Reject(ValheimConnectionStatus.ErrorBanned, "ticket");

            // D — PlayFab/crossplay (:889-911) omitted: Steam-only per I6.

            // E — server full (GetNrOfPlayers() >= 10, :912)
            if (_context.CurrentPlayers >= _context.MaxPlayers)
                return Reject(ValheimConnectionStatus.ErrorFull, "full");

            // F — wrong password (m_serverPassword != passwordHash, :918)
            if (!string.Equals(_context.Password ?? string.Empty, s.PasswordHash ?? string.Empty,
                    StringComparison.Ordinal))
                return Reject(ValheimConnectionStatus.ErrorPassword, "password");

            // G — duplicate (IsConnected(uid), :924)
            if (_connectedUids.Contains(s.Uid))
                return Reject(ValheimConnectionStatus.ErrorAlreadyConnected, "duplicate");

            // H — Lumberjacks seat reservation. NOT a native check: it has no decompile line and
            // runs last so the native verdict always wins first. Surfaces as ErrorFull because that
            // is the only native code whose meaning matches ("the server has no room for you") —
            // the player sees vanilla's full-server screen, and the reason rides failed_check into
            // the server log for the operator (plan §6: only the int reaches the client).
            if (SeatUnavailableFor(s.Uid, nowUtc, lastActivityUtc))
                return Reject(ValheimConnectionStatus.ErrorFull, "capacity_reserved");

            // All pass ⇒ server replies PeerInfo and AddPeer(zdoMan)/AddPeer(routedRpc) (:975-976).
            var reply = new ValheimServerPeerInfo(
                _context.ServerUid, _context.VersionString, _context.NetworkVersion,
                _context.WorldName, _context.Seed, _context.SeedName, _context.WorldUid,
                _context.WorldGenVersion, _context.NetTime);
            return new ValheimHandshakeGateResult(true, true, null, null, null, reply);
        }

        /// <summary>
        /// True when every seat is spoken for by someone other than <paramref name="uid"/>.
        ///
        /// Expiry is passive — evaluated here, on the request that cares — so there is no sweeper
        /// thread and no lock beyond the one SubmitPeerInfo already holds. A seat counts as live
        /// while EITHER its own grant OR the window's consumer traffic is inside the lease: the
        /// grant covers the gap between the verdict and the consumer's first poll, and the consumer
        /// traffic covers the rest of the session. Window-scoped liveness cannot say WHICH holder
        /// is alive, which is exactly good enough at one seat and is why SeatCapacity > 1 is
        /// honoured for counting but not really meaningful yet.
        /// </summary>
        private bool SeatUnavailableFor(long uid, DateTime nowUtc, DateTime? lastActivityUtc)
        {
            if (_context.SeatCapacity <= 0)
                return false;

            var lease = TimeSpan.FromSeconds(Math.Max(1, _context.SeatLeaseSeconds));
            var activityIsLive = lastActivityUtc is DateTime seen && nowUtc - seen < lease;

            foreach (var (holder, grantedUtc) in _seats)
            {
                // Not reachable while gate G stands: a uid in _seats is always in _connectedUids
                // (both are set on the same accept), so a re-handshake is rejected as a duplicate
                // long before check H. Kept as a guard, not a feature — if G ever stops owning that
                // case, the seat gate must not tell a player their own seat is taken.
                if (holder == uid)
                    return false;
                if (activityIsLive || nowUtc - grantedUtc < lease)
                    return true; // SeatCapacity is validated to 0..1, so one live holder is enough
            }
            return false;
        }

        private void TakeSeat(long uid, DateTime nowUtc)
        {
            if (_context.SeatCapacity <= 0)
                return;
            // Reaching here means SeatUnavailableFor found no live holder, so any surviving entry
            // has lapsed by definition and clearing is safe. Doing it this way keeps ONE liveness
            // rule in the class: an earlier revision re-derived staleness here from grant age alone
            // while the gate counted grant age OR activity, so a live holder could be purged by the
            // very join their own liveness should have blocked.
            _seats.Clear();
            _seats[uid] = nowUtc;
        }

        private bool IsAllowed(string host, string name)
        {
            if (_context.BannedHosts.Contains(host, StringComparer.Ordinal)
                || _context.BannedHosts.Contains(name, StringComparer.Ordinal))
                return false;
            if (_context.PermittedHosts.Count > 0
                && !_context.PermittedHosts.Contains(host, StringComparer.Ordinal))
                return false;
            return true;
        }

        private static ValheimHandshakeGateResult Reject(ValheimConnectionStatus status, string check) =>
            new(false, false, (int)status, status.ToString(), check, null);

        private void Touch()
        {
            var now = DateTime.UtcNow;
            _firstUtc ??= now;
            _lastUtc = now;
        }

        public ValheimHandshakeWindowStatus Status(string windowId)
        {
            lock (_gate)
            {
                return new ValheimHandshakeWindowStatus(
                    windowId,
                    _context.NetworkVersion,
                    !string.IsNullOrEmpty(_context.Password),
                    _context.MaxPlayers,
                    _context.CurrentPlayers,
                    _context.RequireSteamTicket,
                    _context.BannedHosts.Count,
                    _context.PermittedHosts.Count,
                    _begins,
                    _accepted,
                    _rejected,
                    _steadyState,
                    _byCode.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                    new List<ValheimHandshakeExchangeRecord>(_exchanges),
                    _firstUtc,
                    _lastUtc);
            }
        }

        public static ValheimHandshakeWindowStatus Empty(string windowId) =>
            new(windowId, NetworkVersion, false, MaxPlayers, 0, true, 0, 0,
                0, 0, 0, 0, new Dictionary<string, long>(),
                new List<ValheimHandshakeExchangeRecord>(), null, null);
    }
}
