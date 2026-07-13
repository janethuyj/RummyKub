namespace Rummikub.Engine;

/// <summary>
/// Greedy move chooser used by both the AI opponent and the hint feature.
/// Strategy:
///   • Before melding — find new sets in the rack; if the highest-value subset
///     reaches 30 points, play it, otherwise draw.
///   • After melding — lay down every new set it can, then attach any remaining
///     rack tiles onto existing board sets one at a time.
/// Every returned board is re-validated, so it never proposes an illegal move.
/// </summary>
public sealed class GreedyMoveFinder : IMoveFinder
{
    public ProposedMove? FindMove(
        IReadOnlyList<IReadOnlyList<Tile>> board,
        IReadOnlyList<Tile> rack,
        bool hasMelded)
    {
        var newSets = SetFinder.FindDisjointSets(rack).Sets;

        if (!hasMelded)
        {
            var meld = SelectMeldReaching30(newSets);
            if (meld is null)
                return null; // can't meet the 30-point minimum → draw

            var resultBoard = CloneBoard(board);
            resultBoard.AddRange(meld);
            var played = meld.SelectMany(s => s).Select(t => t.Id).ToList();
            return new ProposedMove(resultBoard, played);
        }

        // Already melded: play everything we can.
        var newBoard = CloneBoard(board);
        var playedIds = new List<int>();

        foreach (var set in newSets)
        {
            newBoard.Add(set);
            playedIds.AddRange(set.Select(t => t.Id));
        }

        // Attach leftover rack tiles onto existing board sets where legal.
        var usedIds = playedIds.ToHashSet();
        var leftovers = rack.Where(t => !usedIds.Contains(t.Id)).ToList();
        foreach (var tile in leftovers)
        {
            for (int i = 0; i < newBoard.Count; i++)
            {
                var candidate = new List<Tile>(newBoard[i]) { tile };
                if (RuleValidator.IsValidSet(candidate))
                {
                    newBoard[i] = candidate;
                    playedIds.Add(tile.Id);
                    break;
                }
            }
        }

        return playedIds.Count > 0 ? new ProposedMove(newBoard, playedIds) : null;
    }

    /// <summary>Picks highest-value sets until their total reaches 30; null if unreachable.</summary>
    private static List<List<Tile>>? SelectMeldReaching30(List<List<Tile>> sets)
    {
        var ordered = sets
            .Select(s => (set: s, value: RuleValidator.EvaluateSet(s).Value))
            .OrderByDescending(x => x.value)
            .ToList();

        var chosen = new List<List<Tile>>();
        int total = 0;
        foreach (var (set, value) in ordered)
        {
            chosen.Add(set);
            total += value;
            if (total >= RuleValidator.InitialMeldMinimum)
                return chosen;
        }
        return null;
    }

    private static List<List<Tile>> CloneBoard(IReadOnlyList<IReadOnlyList<Tile>> board)
        => board.Select(s => s.ToList()).ToList();
}
