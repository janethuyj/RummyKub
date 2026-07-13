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
    /// Run = consecutive numbers, same colour, length ≥ 3, within 1..13. Jokers
    /// fill interior gaps and may extend either end. Value = sum of the numbers in
    /// the resulting consecutive block; leftover jokers extend upward (higher value)
    /// when there is room, matching a player's incentive at meld time.
    /// </summary>
    private static SetEvaluation EvaluateRun(IReadOnlyList<Tile> tiles)
    {
        if (tiles.Count < MinSetSize)
            return SetEvaluation.Invalid;

        var nonJokers = tiles.Where(t => !t.IsJoker).ToList();
        int jokers = tiles.Count - nonJokers.Count;

        // All jokers: any consecutive block of length 3..13 works. Give it the
        // lowest such block for a deterministic (small) value.
        if (nonJokers.Count == 0)
        {
            if (tiles.Count > 13) return SetEvaluation.Invalid;
            int sumLow = Enumerable.Range(1, tiles.Count).Sum();
            return new SetEvaluation(SetKind.Run, sumLow);
        }

        // Single colour.
        var color = nonJokers[0].Color;
        if (nonJokers.Any(t => t.Color != color))
            return SetEvaluation.Invalid;

        // Distinct numbers (no duplicate values within a run).
        var numbers = nonJokers.Select(t => t.Number).ToList();
        if (numbers.Distinct().Count() != numbers.Count)
            return SetEvaluation.Invalid;

        int min = numbers.Min();
        int max = numbers.Max();
        int span = max - min + 1;           // positions the concrete tiles straddle
        int interiorGaps = span - nonJokers.Count;
        if (interiorGaps < 0 || interiorGaps > jokers)
            return SetEvaluation.Invalid;   // duplicate/overlap or not enough jokers

        int leftover = jokers - interiorGaps;   // jokers that must extend the ends
        int roomBelow = min - 1;                 // positions down to 1
        int roomAbove = 13 - max;                // positions up to 13
        if (leftover > roomBelow + roomAbove)
            return SetEvaluation.Invalid;

        // Extend upward first (higher value), then downward.
        int extendUp = Math.Min(leftover, roomAbove);
        int extendDown = leftover - extendUp;
        int runStart = min - extendDown;
        int runEnd = max + extendUp;

        if (runEnd - runStart + 1 != tiles.Count)
            return SetEvaluation.Invalid; // safety: block length must equal tile count

        int value = SumRange(runStart, runEnd);
        return new SetEvaluation(SetKind.Run, value);
    }

    private static int SumRange(int from, int to) => (from + to) * (to - from + 1) / 2;
}
