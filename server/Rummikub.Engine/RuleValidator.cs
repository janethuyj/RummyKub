namespace Rummikub.Engine;

/// <summary>
/// Pure rules engine: validates individual sets (groups / runs, with joker
/// substitution), whole boards, and the 30-point initial meld. No game state,
/// no randomness — every method is a deterministic function of its inputs so it
/// can be unit tested exhaustively.
/// </summary>
public static class RuleValidator
{
    public const int MinSetSize = 3;
    public const int InitialMeldMinimum = 30;

    /// <summary>Validates a candidate set and computes its point value.</summary>
    public static SetEvaluation EvaluateSet(IReadOnlyList<Tile> tiles)
    {
        if (tiles is null || tiles.Count < MinSetSize)
            return SetEvaluation.Invalid;

        // A set is valid if it forms EITHER a group or a run. Try both.
        var asGroup = EvaluateGroup(tiles);
        if (asGroup.IsValid)
            return asGroup;

        return EvaluateRun(tiles);
    }

    public static bool IsValidSet(IReadOnlyList<Tile> tiles) => EvaluateSet(tiles).IsValid;

    /// <summary>True when every set on the board is a valid group or run.</summary>
    public static bool IsBoardValid(IEnumerable<IReadOnlyList<Tile>> sets)
        => sets.All(IsValidSet);

    /// <summary>
    /// Group = same number, distinct colours, 3 or 4 tiles. Jokers fill missing
    /// colours. Value = number × tile count.
    /// </summary>
    private static SetEvaluation EvaluateGroup(IReadOnlyList<Tile> tiles)
    {
        if (tiles.Count is < 3 or > 4)
            return SetEvaluation.Invalid;

        var nonJokers = tiles.Where(t => !t.IsJoker).ToList();
        int jokers = tiles.Count - nonJokers.Count;

        // All jokers: a 3-4 tile all-joker group is trivially satisfiable.
        if (nonJokers.Count == 0)
            return new SetEvaluation(SetKind.Group, 0); // no fixed number → value 0

        int number = nonJokers[0].Number;
        if (nonJokers.Any(t => t.Number != number))
            return SetEvaluation.Invalid;

        // Colours must be distinct among the concrete tiles.
        if (nonJokers.Select(t => t.Color).Distinct().Count() != nonJokers.Count)
            return SetEvaluation.Invalid;

        // Enough distinct colours must remain for the jokers to occupy.
        if (jokers > 4 - nonJokers.Count)
            return SetEvaluation.Invalid;

        return new SetEvaluation(SetKind.Group, number * tiles.Count);
    }

    /// <summary>
    /// Run = consecutive numbers of one colour, length ≥ 3. Validation is
    /// positional: each tile's number is fixed by its position and a joker takes the
    /// number implied by where it sits, which must stay within 1..13. So a joker
    /// placed after a 13 (a 14) is rejected, matching the physical game. Value = sum
    /// of the numbers in the run.
    /// </summary>
    private static SetEvaluation EvaluateRun(IReadOnlyList<Tile> tiles)
    {
        if (tiles.Count < MinSetSize)
            return SetEvaluation.Invalid;

        // The first concrete tile fixes the colour and the number implied at index 0.
        TileColor color = default;
        int? baseNumber = null;
        for (int i = 0; i < tiles.Count; i++)
        {
            if (tiles[i].IsJoker) continue;
            color = tiles[i].Color;
            baseNumber = tiles[i].Number - i;
            break;
        }

        // All jokers: a length 3..13 run is satisfiable (treat as 1..n for a value).
        if (baseNumber is null)
            return tiles.Count <= 13
                ? new SetEvaluation(SetKind.Run, SumRange(1, tiles.Count))
                : SetEvaluation.Invalid;

        int start = baseNumber.Value;
        int end = start + tiles.Count - 1;
        if (start < 1 || end > 13)
            return SetEvaluation.Invalid; // would run below 1 or above 13 (e.g. joker as a 14)

        for (int i = 0; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            if (tile.IsJoker) continue;                         // joker = the implied number
            if (tile.Color != color || tile.Number != start + i)
                return SetEvaluation.Invalid;                   // wrong colour or out of sequence
        }

        return new SetEvaluation(SetKind.Run, SumRange(start, end));
    }

    private static int SumRange(int from, int to) => (from + to) * (to - from + 1) / 2;
}
