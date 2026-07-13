using Rummikub.Engine;

namespace Rummikub.Server.Game;

/// <summary>
/// Server-side state for one room: the pure <see cref="GameSession"/> plus the
/// things the engine doesn't care about — connection mapping, host, per-turn
/// timer setting and the current turn's deadline. A <see cref="Gate"/> serializes
/// all mutations so concurrent SignalR calls and the async AI/timer loop can't
/// interleave.
/// </summary>
public sealed class GameRoom
{
    public required string Code { get; init; }
    public GameSession Session { get; } = new();
    public string? HostId { get; set; }

    /// <summary>Per-turn time limit in seconds; 0 = off.</summary>
    public int TimerSeconds { get; set; }

    /// <summary>Unix-ms deadline for the current turn, or null when the timer is off.</summary>
    public long? TurnDeadlineUnixMs { get; set; }

    /// <summary>Token used to cancel the in-flight turn timer when the turn changes.</summary>
    public CancellationTokenSource? TurnTimerCts { get; set; }

    public SemaphoreSlim Gate { get; } = new(1, 1);

    // playerId -> connectionId (absent/null when disconnected)
    private readonly Dictionary<string, string?> _connections = new();

    public void BindConnection(string playerId, string connectionId) => _connections[playerId] = connectionId;

    public void MarkDisconnected(string connectionId)
    {
        foreach (var key in _connections.Keys.ToList())
            if (_connections[key] == connectionId)
                _connections[key] = null;
    }

    public string? ConnectionFor(string playerId) => _connections.GetValueOrDefault(playerId);
    public bool IsConnected(string playerId) => ConnectionFor(playerId) is not null;

    public string? PlayerIdForConnection(string connectionId) =>
        _connections.FirstOrDefault(kv => kv.Value == connectionId).Key;

    public IEnumerable<string> ConnectedConnectionIds =>
        _connections.Values.Where(c => c is not null)!.Cast<string>();
}
