namespace Rummikub.Engine;

/// <summary>
/// Greedy set discovery shared by the AI and the rack organizer. Given a bag of
/// tiles it extracts a collection of disjoint valid sets (runs/groups), then uses
/// any jokers to extend those sets or complete near-sets. Greedy — not guaranteed
/// optimal — but every set it returns is validated, so it never produces an
/// illegal set. An optimal solver can replace this later behind the same surface.
/// </summary>
public static class SetFinder
{
    public sealed record Result(List<List<Tile>> Sets, List<Tile> Leftovers);

    public static Result FindDisjointSets(IReadOnlyList<Tile> tiles)
    {
        var remaining = tiles.Where(t => !t.IsJoker).ToList();
        var jokers = tiles.Where(t => t.IsJoker).ToList();
        var sets = new List<List<Tile>>();

        // Repeatedly pull out the largest valid set until none remain.
        while (true)
        {
            var best = FindLargestSet(remaining);
            if (best is null || best.Count < RuleValidator.MinSetSize)
                break;
            foreach (var t in best)
                remaining.Remove(t);
            sets.Add(best);
        }

        // Spend jokers: extend an existing set, else complete a near-set from leftovers.
        foreach (var joker in jokers)
        {
            if (TryExtendExistingSet(sets, joker))
                continue;
            if (TryCompleteNearSet(remaining, joker, sets))
                continue;
            // Unused joker falls through to leftovers.
            remaining.Add(joker);
        }

        return new Result(sets, remaining);
    }

    /// <summary>Finds the single valid set using the most tiles among the given tiles (no jokers).</summary>
    private static List<Tile>? FindLargestSet(List<Tile> tiles)
    {
        List<Tile>? best = null;

        // Runs: per colour, find maximal consecutive chains (one tile instance per number).
        foreach (var colorGroup in tiles.GroupBy(t => t.Color))
        {
            var byNumber = colorGroup
                .GroupBy(t => t.Number)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.First());
            var numbers = byNumber.Keys.OrderBy(x => x).ToList();

            int i = 0;
            while (i < numbers.Count)
            {
                int j = i;
                while (j + 1 < numbers.Count && numbers[j + 1] == numbers[j] + 1)
                    j++;
                int chainLen = j - i + 1;
                if (chainLen >= RuleValidator.MinSetSize)
                {
                    var run = numbers.GetRange(i, chainLen).Select(n => byNumber[n]).ToList();
                    if (best is null || run.Count > best.Count)
                        best = run;
                }
                i = j + 1;
            }
        }

        // Groups: per number, distinct colours (3 or 4).
        foreach (var numberGroup in tiles.GroupBy(t => t.Number))
        {
            var distinctByColor = numberGroup
                .GroupBy(t => t.Color)
                .Select(g => g.First())
                .ToList();
            if (distinctByColor.Count >= RuleValidator.MinSetSize &&
                (best is null || distinctByColor.Count > best.Count))
            {
                best = distinctByColor;
            }
        }

        return best;
    }

    private static bool TryExtendExistingSet(List<List<Tile>> sets, Tile joker)
    {
        // Insert the joker at whichever position keeps the set valid (runs are now
        // order-sensitive, so a joker must go where its implied number is 1..13).
        foreach (var set in sets)
        {
            for (int pos = 0; pos <= set.Count; pos++)
            {
                var candidate = new List<Tile>(set);
                candidate.Insert(pos, joker);
                if (RuleValidator.IsValidSet(candidate))
                {
                    set.Clear();
                    set.AddRange(candidate);
                    return true;
                }
            }
        }
        return false;
    }

    private static bool TryCompleteNearSet(List<Tile> remaining, Tile joker, List<List<Tile>> sets)
    {
        for (int a = 0; a < remaining.Count; a++)
        {
            for (int b = a + 1; b < remaining.Count; b++)
            {
                // Try both orders of the pair and every joker position between them.
                foreach (var pair in new[] { new[] { remaining[a], remaining[b] }, new[] { remaining[b], remaining[a] } })
                {
                    for (int pos = 0; pos <= 2; pos++)
                    {
                        var candidate = new List<Tile>(pair);
                        candidate.Insert(pos, joker);
                        if (RuleValidator.IsValidSet(candidate))
                        {
                            remaining.RemoveAt(b); // higher index first
                            remaining.RemoveAt(a);
                            sets.Add(candidate);
                            return true;
                        }
                    }
                }
            }
        }
        return false;
    }
}
