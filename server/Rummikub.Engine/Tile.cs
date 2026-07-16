namespace Rummikub.Engine;

/// <summary>
/// A single physical tile. <see cref="Id"/> is unique across the 106-tile pool so
/// that the two copies of each (colour, number) — and the two jokers — remain
/// distinguishable, which matters for undo/redo and for tracking tile ownership.
/// For a joker, <see cref="Number"/> is unspecified and <see cref="Color"/> is purely
/// decorative — it says which of the two printed jokers this is (red or black), the way a
/// real set does, and never affects the rules. The value a joker represents is derived
/// from the set it sits in (see <see cref="RuleValidator"/>).
/// </summary>
public sealed record Tile
{
    public required int Id { get; init; }
    public TileColor Color { get; init; }
    public int Number { get; init; }
    public bool IsJoker { get; init; }

    public static Tile Numbered(int id, TileColor color, int number)
    {
        if (number is < 1 or > 13)
            throw new ArgumentOutOfRangeException(nameof(number), number, "Tile number must be 1..13.");
        return new Tile { Id = id, Color = color, Number = number };
    }

    /// <param name="color">Which printed joker this is. Decorative only — the rules ignore it.</param>
    public static Tile Joker(int id, TileColor color = TileColor.Red)
        => new() { Id = id, Color = color, IsJoker = true };

    public override string ToString()
        => IsJoker ? $"J{Color.ToString()[..1]}#{Id}" : $"{Color.ToString()[..1]}{Number}#{Id}";
}
