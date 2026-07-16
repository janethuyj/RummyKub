namespace Rummikub.Engine;

/// <summary>
/// Move chooser used by both the AI opponent and the hint feature. It defers to
/// <see cref="TurnSolver"/>, which searches every legal layout of the board and rack
/// together and returns the one playing the most tiles — so unlike a greedy chooser it
/// will happily break up and rebuild the whole board to squeeze one more tile out.
/// The one tile it is stingy with is a joker, which it holds back unless spending it
/// frees at least two other tiles or empties the rack outright.
/// Strategy:
///   • Before melding — the board is off limits, so it looks for the best set of plays
///     built purely from the rack worth 30+ points; if there is none, it draws.
///   • After melding — it rearranges the board freely to play as many tiles as it can.
/// Returns <c>null</c> when there is nothing to play, meaning "draw instead".
/// Every returned board is re-validated, so it never proposes an illegal move.
/// </summary>
public sealed class OptimalMoveFinder : IMoveFinder
{
    public ProposedMove? FindMove(
        IReadOnlyList<IReadOnlyList<Tile>> board,
        IReadOnlyList<Tile> rack,
        bool hasMelded)
    {
        var boardTiles = board.SelectMany(s => s).ToList();

        // Before the initial meld the existing sets may not be touched, so they are kept
        // aside and only the rack is handed to the solver.
        var input = new SolverInput(
            Mandatory: CountByTile(hasMelded ? boardTiles : Array.Empty<Tile>()),
            Optional: CountByTile(rack),
            MandatoryJokers: hasMelded ? boardTiles.Count(t => t.IsJoker) : 0,
            OptionalJokers: rack.Count(t => t.IsJoker),
            RequireMeldPoints: !hasMelded);

        var solution = TurnSolver.Solve(input);
        if (solution is null || solution.OptionalTilesUsed == 0)
            return null;

        var laid = AssignTiles(solution.Sets, boardTiles, rack, hasMelded);
        var newBoard = hasMelded ? laid : board.Select(s => s.ToList()).Concat(laid).ToList();

        var rackIds = rack.Select(t => t.Id).ToHashSet();
        var playedIds = newBoard.SelectMany(s => s).Select(t => t.Id).Where(rackIds.Contains).ToList();
        if (playedIds.Count == 0)
            return null;

        // Belt and braces: never hand the caller a move the rules would reject.
        if (!TurnValidator.Validate(board, rack, newBoard, hasMelded).Ok)
            return null;

        return new ProposedMove(newBoard, playedIds);
    }

    private static int[,] CountByTile(IEnumerable<Tile> tiles)
    {
        var counts = new int[4, 14];
        foreach (var tile in tiles.Where(t => !t.IsJoker))
            counts[(int)tile.Color, tile.Number]++;
        return counts;
    }

    /// <summary>
    /// Turns the solver's abstract layout back into real tiles. Board tiles are handed out
    /// before rack tiles for the same (colour, number), which is what keeps every board tile
    /// on the board — the solver already guarantees each is used at least as often as it appears.
    /// </summary>
    private static List<List<Tile>> AssignTiles(
        List<List<Slot>> sets,
        IReadOnlyList<Tile> boardTiles,
        IReadOnlyList<Tile> rack,
        bool boardIsInPlay)
    {
        var available = boardIsInPlay ? boardTiles.Concat(rack) : rack;
        var pools = new Dictionary<(TileColor, int), Queue<Tile>>();
        var jokers = new Queue<Tile>();

        foreach (var tile in available)
        {
            if (tile.IsJoker)
            {
                jokers.Enqueue(tile);
                continue;
            }
            var key = (tile.Color, tile.Number);
            if (!pools.TryGetValue(key, out var pool))
                pools[key] = pool = new Queue<Tile>();
            pool.Enqueue(tile);
        }

        return sets
            .Select(set => set
                .Select(slot => slot.IsJoker ? jokers.Dequeue() : pools[(slot.Color, slot.Number)].Dequeue())
                .ToList())
            .ToList();
    }
}
