namespace Rummikub.Engine;

public enum GameStatus
{
    Lobby,
    Playing,
    Finished
}

/// <summary>Outcome of a session action (move, draw, etc.).</summary>
public readonly record struct ActionResult(bool Ok, string? Error)
{
    public static ActionResult Success => new(true, null);
    public static ActionResult Fail(string error) => new(false, error);
}

/// <summary>
/// Pure, network-free orchestration of one Rummikub round: players, board, draw
/// pile, turn order, move application and win detection. The server layer wraps
/// this with SignalR, per-turn timers and AI scheduling. Deterministic given its
/// RNG, so full games can be unit tested.
/// </summary>
public sealed class GameSession
{
    public const int StartingRackSize = 14;
    public const int MaxPlayers = 4;

    private readonly List<Player> _players = new();
    private readonly List<List<Tile>> _board = new();
    private readonly List<Tile> _drawPile = new();
    private int _consecutivePasses; // forced passes (empty pool) since the last play

    public IReadOnlyList<Player> Players => _players;
    public IReadOnlyList<IReadOnlyList<Tile>> Board => _board;
    public GameStatus Status { get; private set; } = GameStatus.Lobby;
    public int CurrentPlayerIndex { get; private set; }
    public string? WinnerId { get; private set; }
    public int DrawPileCount => _drawPile.Count;

    public Player? CurrentPlayer =>
        Status == GameStatus.Playing && _players.Count > 0 ? _players[CurrentPlayerIndex] : null;

    // ---- Lobby ----

    public ActionResult AddPlayer(string id, string name, bool isAi)
    {
        if (Status != GameStatus.Lobby)
            return ActionResult.Fail("Game already started.");
        if (_players.Count >= MaxPlayers)
            return ActionResult.Fail("Room is full (4 players max).");
        if (_players.Any(p => p.Id == id))
            return ActionResult.Fail("Player already in room.");
        _players.Add(new Player { Id = id, Name = name, IsAi = isAi });
        return ActionResult.Success;
    }

    public void RemovePlayer(string id)
    {
        if (Status == GameStatus.Lobby)
            _players.RemoveAll(p => p.Id == id);
    }

    public ActionResult Start(Random rng)
    {
        if (Status != GameStatus.Lobby)
            return ActionResult.Fail("Game already started.");
        if (_players.Count < 2)
            return ActionResult.Fail("Need at least 2 players.");

        _board.Clear();
        _drawPile.Clear();
        _drawPile.AddRange(TilePool.BuildShuffled(rng));

        foreach (var player in _players)
        {
            player.Rack.Clear();
            player.HasMadeInitialMeld = false;
            for (int i = 0; i < StartingRackSize; i++)
                player.Rack.Add(DrawTile()!);
        }

        CurrentPlayerIndex = 0;
        WinnerId = null;
        _consecutivePasses = 0;
        Status = GameStatus.Playing;
        return ActionResult.Success;
    }

    /// <summary>Re-deal and start a fresh round with the same players (after a win).</summary>
    public ActionResult Restart(Random rng)
    {
        if (Status != GameStatus.Finished)
            return ActionResult.Fail("Can only start a new game once the round has finished.");
        Status = GameStatus.Lobby;
        return Start(rng);
    }

    // ---- Turn actions ----

    /// <summary>Draw one tile (if any remain) and pass the turn.</summary>
    public ActionResult DrawAndPass(string playerId)
    {
        if (!EnsureCurrent(playerId, out var error))
            return ActionResult.Fail(error);

        var tile = DrawTile();
        if (tile is not null)
        {
            CurrentPlayer!.Rack.Add(tile);
            _consecutivePasses = 0; // drew a tile — the game is still progressing
        }
        else
        {
            _consecutivePasses++; // forced pass: the pool is empty
        }

        AdvanceTurn();

        // If the pool is empty and every player has passed in turn, the game is
        // deadlocked. End it; the player with the lowest hand value wins.
        if (Status == GameStatus.Playing && _consecutivePasses >= _players.Count)
        {
            Status = GameStatus.Finished;
            WinnerId = _players.OrderBy(HandValue).First().Id;
        }

        return ActionResult.Success;
    }

    /// <summary>Sum of a rack's tile values (jokers count as 30) — lower is better.</summary>
    private static int HandValue(Player p) => p.Rack.Sum(t => t.IsJoker ? 30 : t.Number);

    /// <summary>
    /// Commit a rearranged board expressed as tile ids. Ids are resolved against the
    /// current board plus the player's rack, so a client cannot introduce tiles it
    /// does not hold. Validates, applies, updates meld status, and checks for a win.
    /// </summary>
    public ActionResult CommitMove(string playerId, IReadOnlyList<IReadOnlyList<int>> proposedBoardIds)
    {
        if (!EnsureCurrent(playerId, out var error))
            return ActionResult.Fail(error);

        var player = CurrentPlayer!;

        var known = _board.SelectMany(s => s)
            .Concat(player.Rack)
            .ToDictionary(t => t.Id);

        var proposedBoard = new List<IReadOnlyList<Tile>>();
        foreach (var set in proposedBoardIds)
        {
            var tiles = new List<Tile>();
            foreach (var id in set)
            {
                if (!known.TryGetValue(id, out var tile))
                    return ActionResult.Fail("Move references a tile you do not have.");
                tiles.Add(tile);
            }
            proposedBoard.Add(tiles);
        }

        var startBoard = _board.Select(s => (IReadOnlyList<Tile>)s).ToList();
        var validation = TurnValidator.Validate(startBoard, player.Rack, proposedBoard, player.HasMadeInitialMeld);
        if (!validation.Ok)
            return ActionResult.Fail(validation.Error!);

        // Apply: replace board, remove played tiles from rack, update meld status.
        var proposedIds = proposedBoard.SelectMany(s => s).Select(t => t.Id).ToHashSet();
        var boardStartIds = _board.SelectMany(s => s).Select(t => t.Id).ToHashSet();
        var playedIds = proposedIds.Except(boardStartIds).ToHashSet();

        _board.Clear();
        _board.AddRange(proposedBoard.Select(s => s.ToList()));
        player.Rack.RemoveAll(t => playedIds.Contains(t.Id));
        player.HasMadeInitialMeld = true;
        _consecutivePasses = 0; // a play breaks any deadlock

        if (player.Rack.Count == 0)
        {
            Status = GameStatus.Finished;
            WinnerId = player.Id;
            return ActionResult.Success;
        }

        AdvanceTurn();
        return ActionResult.Success;
    }

    // ---- Helpers ----

    private Tile? DrawTile()
    {
        if (_drawPile.Count == 0)
            return null;
        var tile = _drawPile[^1];
        _drawPile.RemoveAt(_drawPile.Count - 1);
        return tile;
    }

    private void AdvanceTurn()
    {
        if (Status != GameStatus.Playing)
            return;
        CurrentPlayerIndex = (CurrentPlayerIndex + 1) % _players.Count;
    }

    private bool EnsureCurrent(string playerId, out string error)
    {
        if (Status != GameStatus.Playing)
        {
            error = "Game is not in progress.";
            return false;
        }
        if (CurrentPlayer!.Id != playerId)
        {
            error = "It is not your turn.";
            return false;
        }
        error = string.Empty;
        return true;
    }
}
