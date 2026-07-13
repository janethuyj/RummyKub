using Microsoft.AspNetCore.SignalR;
using Rummikub.Engine;
using Rummikub.Server.Contracts;
using Rummikub.Server.Hubs;

namespace Rummikub.Server.Game;

/// <summary>
/// Owns all game operations and side effects: mutating the session, broadcasting
/// personalized state to each connection, running the per-turn timer, and driving
/// AI turns. The <see cref="GameHub"/> is a thin forwarder into this singleton.
/// Every mutation is serialized by the room's gate.
/// </summary>
public sealed class GameCoordinator
{
    private readonly IRoomStore _store;
    private readonly IHubContext<GameHub> _hub;
    private readonly IMoveFinder _finder = new GreedyMoveFinder();
    private const int AiThinkMs = 900;

    public GameCoordinator(IRoomStore store, IHubContext<GameHub> hub)
    {
        _store = store;
        _hub = hub;
    }

    // ---- Lobby ----

    public async Task<JoinResult> CreateRoomAsync(string connectionId, string playerName)
    {
        var room = _store.Create();
        var playerId = NewPlayerId();
        await room.Gate.WaitAsync();
        try
        {
            room.Session.AddPlayer(playerId, CleanName(playerName, room.Session.Players.Count), isAi: false);
            room.HostId = playerId;
            room.BindConnection(playerId, connectionId);
        }
        finally { room.Gate.Release(); }

        await _hub.Groups.AddToGroupAsync(connectionId, room.Code);
        await BroadcastAsync(room);
        return new JoinResult(true, null, room.Code, playerId);
    }

    public async Task<JoinResult> JoinRoomAsync(string connectionId, string code, string playerName)
    {
        var room = _store.Get(code);
        if (room is null)
            return new JoinResult(false, "Room not found.", null, null);

        var playerId = NewPlayerId();
        ActionResult add;
        await room.Gate.WaitAsync();
        try
        {
            add = room.Session.AddPlayer(playerId, CleanName(playerName, room.Session.Players.Count), isAi: false);
            if (add.Ok)
                room.BindConnection(playerId, connectionId);
        }
        finally { room.Gate.Release(); }

        if (!add.Ok)
            return new JoinResult(false, add.Error, null, null);

        await _hub.Groups.AddToGroupAsync(connectionId, room.Code);
        await BroadcastAsync(room);
        return new JoinResult(true, null, room.Code, playerId);
    }

    /// <summary>Rebind a returning player's seat to a new connection (reconnect).</summary>
    public async Task<JoinResult> RejoinAsync(string connectionId, string code, string playerId)
    {
        var room = _store.Get(code);
        if (room is null)
            return new JoinResult(false, "Room not found.", null, null);

        bool known;
        await room.Gate.WaitAsync();
        try
        {
            known = room.Session.Players.Any(p => p.Id == playerId);
            if (known)
                room.BindConnection(playerId, connectionId);
        }
        finally { room.Gate.Release(); }

        if (!known)
            return new JoinResult(false, "Seat not found in this room.", null, null);

        await _hub.Groups.AddToGroupAsync(connectionId, room.Code);
        await BroadcastAsync(room);
        return new JoinResult(true, null, room.Code, playerId);
    }

    public async Task<ActionResultDto> AddAiAsync(string connectionId, string code)
    {
        var room = _store.Get(code);
        if (room is null) return new ActionResultDto(false, "Room not found.");

        ActionResult result;
        await room.Gate.WaitAsync();
        try
        {
            var requester = room.PlayerIdForConnection(connectionId);
            if (requester != room.HostId)
                return new ActionResultDto(false, "Only the host can add AI players.");
            int botNumber = room.Session.Players.Count(p => p.IsAi) + 1;
            result = room.Session.AddPlayer("ai-" + NewPlayerId(), $"Bot {botNumber}", isAi: true);
        }
        finally { room.Gate.Release(); }

        if (result.Ok) await BroadcastAsync(room);
        return ActionResultDto.From(result);
    }

    public async Task<ActionResultDto> StartGameAsync(string connectionId, string code)
    {
        var room = _store.Get(code);
        if (room is null) return new ActionResultDto(false, "Room not found.");

        ActionResult result;
        await room.Gate.WaitAsync();
        try
        {
            if (room.PlayerIdForConnection(connectionId) != room.HostId)
                return new ActionResultDto(false, "Only the host can start the game.");
            result = room.Session.Start(new Random());
        }
        finally { room.Gate.Release(); }

        if (result.Ok) await AfterTurnAsync(room);
        return ActionResultDto.From(result);
    }

    // ---- Turn actions ----

    public async Task<ActionResultDto> DrawAsync(string connectionId, string code)
    {
        var room = _store.Get(code);
        if (room is null) return new ActionResultDto(false, "Room not found.");

        ActionResult result;
        await room.Gate.WaitAsync();
        try
        {
            var playerId = room.PlayerIdForConnection(connectionId);
            result = playerId is null
                ? ActionResult.Fail("You are not in this room.")
                : room.Session.DrawAndPass(playerId);
        }
        finally { room.Gate.Release(); }

        if (result.Ok) await AfterTurnAsync(room);
        return ActionResultDto.From(result);
    }

    public async Task<ActionResultDto> CommitAsync(string connectionId, string code, IReadOnlyList<IReadOnlyList<int>> board)
    {
        var room = _store.Get(code);
        if (room is null) return new ActionResultDto(false, "Room not found.");

        ActionResult result;
        await room.Gate.WaitAsync();
        try
        {
            var playerId = room.PlayerIdForConnection(connectionId);
            result = playerId is null
                ? ActionResult.Fail("You are not in this room.")
                : room.Session.CommitMove(playerId, board);
        }
        finally { room.Gate.Release(); }

        if (result.Ok) await AfterTurnAsync(room);
        return ActionResultDto.From(result);
    }

    public async Task<HintDto> HintAsync(string connectionId, string code)
    {
        var room = _store.Get(code);
        if (room is null) return new HintDto(true, Array.Empty<int>(), null);

        await room.Gate.WaitAsync();
        try
        {
            var playerId = room.PlayerIdForConnection(connectionId);
            var player = room.Session.Players.FirstOrDefault(p => p.Id == playerId);
            if (player is null || room.Session.CurrentPlayer?.Id != playerId)
                return new HintDto(true, Array.Empty<int>(), null);

            var move = _finder.FindMove(room.Session.Board, player.Rack, player.HasMadeInitialMeld);
            if (move is null || !move.PlaysAnything)
                return new HintDto(true, Array.Empty<int>(), null);

            var boardIds = move.Board.Select(s => (IReadOnlyList<int>)s.Select(t => t.Id).ToList()).ToList();
            return new HintDto(false, move.PlayedTileIds, boardIds);
        }
        finally { room.Gate.Release(); }
    }

    public async Task<ActionResultDto> SetTimerAsync(string connectionId, string code, int seconds)
    {
        if (seconds is not (0 or 30 or 60))
            return new ActionResultDto(false, "Timer must be 0, 30, or 60 seconds.");

        var room = _store.Get(code);
        if (room is null) return new ActionResultDto(false, "Room not found.");

        await room.Gate.WaitAsync();
        try
        {
            if (room.PlayerIdForConnection(connectionId) != room.HostId)
                return new ActionResultDto(false, "Only the host can change the timer.");
            room.TimerSeconds = seconds;
        }
        finally { room.Gate.Release(); }

        await AfterTurnAsync(room); // re-arm with the new setting
        return new ActionResultDto(true, null);
    }

    public async Task HandleDisconnectAsync(string connectionId)
    {
        // We don't know the room from the connection alone; a small scan is fine at this scale.
        // (Left simple: disconnect handling is best-effort; the seat can be reclaimed via Rejoin.)
        await Task.CompletedTask;
    }

    // ---- Post-turn: broadcast, timer, AI ----

    private async Task AfterTurnAsync(GameRoom room)
    {
        ArmTimer(room);
        await BroadcastAsync(room);
        _ = RunAiIfNeededAsync(room);
    }

    private void ArmTimer(GameRoom room)
    {
        room.TurnTimerCts?.Cancel();
        room.TurnTimerCts = null;
        room.TurnDeadlineUnixMs = null;

        var current = room.Session.CurrentPlayer;
        if (room.Session.Status != GameStatus.Playing || room.TimerSeconds <= 0 || current is null || current.IsAi)
            return;

        var cts = new CancellationTokenSource();
        room.TurnTimerCts = cts;
        room.TurnDeadlineUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + room.TimerSeconds * 1000L;
        var deadlinePlayerId = current.Id;

        _ = Task.Run(async () =>
        {
            try { await Task.Delay(room.TimerSeconds * 1000, cts.Token); }
            catch (TaskCanceledException) { return; }

            await room.Gate.WaitAsync();
            bool timedOut = false;
            try
            {
                if (room.Session.Status == GameStatus.Playing &&
                    room.Session.CurrentPlayer?.Id == deadlinePlayerId)
                {
                    room.Session.DrawAndPass(deadlinePlayerId); // auto-draw + pass on timeout
                    timedOut = true;
                }
            }
            finally { room.Gate.Release(); }

            if (timedOut) await AfterTurnAsync(room);
        });
    }

    private async Task RunAiIfNeededAsync(GameRoom room)
    {
        var current = room.Session.CurrentPlayer;
        if (room.Session.Status != GameStatus.Playing || current is null || !current.IsAi)
            return;

        await Task.Delay(AiThinkMs);

        await room.Gate.WaitAsync();
        try
        {
            var player = room.Session.CurrentPlayer;
            if (room.Session.Status != GameStatus.Playing || player is null || !player.IsAi)
                return;

            var move = _finder.FindMove(room.Session.Board, player.Rack, player.HasMadeInitialMeld);
            if (move is not null && move.PlaysAnything)
            {
                var boardIds = move.Board.Select(s => (IReadOnlyList<int>)s.Select(t => t.Id).ToList()).ToList();
                var applied = room.Session.CommitMove(player.Id, boardIds);
                if (!applied.Ok)
                    room.Session.DrawAndPass(player.Id); // fall back to drawing if something slipped
            }
            else
            {
                room.Session.DrawAndPass(player.Id);
            }
        }
        finally { room.Gate.Release(); }

        await AfterTurnAsync(room);
    }

    // ---- Broadcasting ----

    private async Task BroadcastAsync(GameRoom room)
    {
        foreach (var player in room.Session.Players)
        {
            var conn = room.ConnectionFor(player.Id);
            if (conn is null) continue;
            var dto = BuildStateFor(room, player.Id);
            await _hub.Clients.Client(conn).SendAsync("GameState", dto);
        }
    }

    private static GameStateDto BuildStateFor(GameRoom room, string playerId)
    {
        var s = room.Session;
        var players = s.Players
            .Select(p => new PlayerStateDto(
                p.Id, p.Name, p.IsAi, p.Rack.Count, p.HasMadeInitialMeld,
                p.IsAi || room.IsConnected(p.Id), p.Id == room.HostId))
            .ToList();

        var board = s.Board
            .Select(set => (IReadOnlyList<TileDto>)set.Select(TileDto.From).ToList())
            .ToList();

        var yourRack = s.Players.FirstOrDefault(p => p.Id == playerId)?.Rack
            .Select(TileDto.From).ToList() ?? new List<TileDto>();

        return new GameStateDto(
            room.Code,
            s.Status.ToString(),
            players,
            board,
            s.CurrentPlayerIndex,
            s.CurrentPlayer?.Id,
            s.WinnerId,
            s.DrawPileCount,
            room.TimerSeconds,
            room.TurnDeadlineUnixMs,
            playerId,
            yourRack);
    }

    private static string NewPlayerId() => Guid.NewGuid().ToString("N");

    private static string CleanName(string? name, int index)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return $"Player {index + 1}";
        return name.Length > 20 ? name[..20] : name;
    }
}
