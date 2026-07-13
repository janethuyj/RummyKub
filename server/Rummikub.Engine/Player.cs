namespace Rummikub.Engine;

/// <summary>A player's mutable per-round state: their rack and meld status.</summary>
public sealed class Player
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool IsAi { get; init; }

    /// <summary>Tiles currently on the player's rack (hand).</summary>
    public List<Tile> Rack { get; } = new();

    /// <summary>Whether this player has completed their 30-point initial meld.</summary>
    public bool HasMadeInitialMeld { get; set; }

    public bool HasWon => Rack.Count == 0 && HasMadeInitialMeld;
}
