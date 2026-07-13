namespace Rummikub.Engine;

/// <summary>Outcome of validating a proposed board commit.</summary>
public readonly record struct TurnValidation(bool Ok, string? Error)
{
    public static TurnValidation Success => new(true, null);
    public static TurnValidation Fail(string error) => new(false, error);
}

/// <summary>
/// Validates a player's proposed end-of-turn board against the board and rack at
/// the start of the turn. Pure: no mutation, no state. Enforces tile conservation,
/// set validity, and the 30-point initial-meld rule.
/// </summary>
public static class TurnValidator
{
    /// <param name="startBoard">Board as it was at the start of the player's turn.</param>
    /// <param name="startRack">Player's rack at the start of the turn.</param>
    /// <param name="proposedBoard">Board the player wants to commit.</param>
    /// <param name="hasMeldedBefore">Whether the player had already made their initial meld.</param>
    public static TurnValidation Validate(
        IReadOnlyList<IReadOnlyList<Tile>> startBoard,
        IReadOnlyList<Tile> startRack,
        IReadOnlyList<IReadOnlyList<Tile>> proposedBoard,
        bool hasMeldedBefore)
    {
        // 1. Every proposed set must be a valid group or run.
        foreach (var set in proposedBoard)
        {
            if (!RuleValidator.IsValidSet(set))
                return TurnValidation.Fail("Board contains an invalid set.");
        }

        // 2. No tile may appear twice on the board.
        var proposedIds = proposedBoard.SelectMany(s => s).Select(t => t.Id).ToList();
        if (proposedIds.Count != proposedIds.Distinct().Count())
            return TurnValidation.Fail("A tile appears more than once on the board.");

        var proposedIdSet = proposedIds.ToHashSet();
        var startBoardIds = startBoard.SelectMany(s => s).Select(t => t.Id).ToHashSet();
        var rackIds = startRack.Select(t => t.Id).ToHashSet();

        // 3. Tile conservation: every board tile added must come from the rack, and
        //    no existing board tile may be removed (rearrangement keeps all of them).
        var added = proposedIdSet.Except(startBoardIds).ToHashSet();
        if (added.Any(id => !rackIds.Contains(id)))
            return TurnValidation.Fail("Board contains a tile not from your rack or the board.");

        var removed = startBoardIds.Except(proposedIdSet);
        if (removed.Any())
            return TurnValidation.Fail("Existing board tiles may not be removed.");

        // No change at all = not a play (the caller treats this as "draw instead").
        if (added.Count == 0)
            return TurnValidation.Fail("You must place at least one tile.");

        // 4. Initial-meld rule. Until a player has melded, they may only lay down
        //    brand-new sets built entirely from their own rack, worth >= 30 points,
        //    and may not touch existing board sets.
        if (!hasMeldedBefore)
        {
            var startSetSignatures = startBoard
                .Select(s => s.Select(t => t.Id).OrderBy(x => x).ToArray())
                .ToList();

            int meldValue = 0;
            foreach (var set in proposedBoard)
            {
                bool isUnchangedExistingSet = startSetSignatures.Any(sig =>
                    sig.Length == set.Count &&
                    sig.SequenceEqual(set.Select(t => t.Id).OrderBy(x => x)));
                if (isUnchangedExistingSet)
                    continue;

                bool isEntirelyNew = set.All(t => added.Contains(t.Id));
                if (!isEntirelyNew)
                    return TurnValidation.Fail(
                        "Before your first meld you can only lay new sets, not modify the board.");

                meldValue += RuleValidator.EvaluateSet(set).Value;
            }

            if (meldValue < RuleValidator.InitialMeldMinimum)
                return TurnValidation.Fail(
                    $"Your first meld must total at least {RuleValidator.InitialMeldMinimum} points (was {meldValue}).");
        }

        return TurnValidation.Success;
    }
}
