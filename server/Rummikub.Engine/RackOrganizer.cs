namespace Rummikub.Engine;

/// <summary>
/// Auto-organize: reorders a rack so potential sets are grouped together, with the
/// leftover tiles sorted by colour then number (jokers last). Purely cosmetic — it
/// returns the same tiles in a friendlier order and does not change game state.
/// </summary>
public static class RackOrganizer
{
    public sealed record OrganizedRack(List<List<Tile>> Sets, List<Tile> Loose, List<Tile> Ordered);

    public static OrganizedRack Organize(IReadOnlyList<Tile> rack)
    {
        var found = SetFinder.FindDisjointSets(rack);

        var loose = found.Leftovers
            .OrderBy(t => t.IsJoker)                       // jokers last
            .ThenBy(t => t.Color)
            .ThenBy(t => t.Number)
            .ToList();

        var ordered = new List<Tile>();
        foreach (var set in found.Sets)
            ordered.AddRange(set);
        ordered.AddRange(loose);

        return new OrganizedRack(found.Sets, loose, ordered);
    }
}
