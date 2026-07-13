namespace Rummikub.Engine;

/// <summary>
/// Builds and shuffles the standard 106-tile pool: for each of the 4 colours,
/// numbers 1..13 in two copies (104 tiles), plus 2 jokers. The RNG seed is
/// injectable so shuffles are reproducible in tests.
/// </summary>
public static class TilePool
{
    public const int TotalTiles = 106;

    /// <summary>Returns the 106 tiles in canonical (unshuffled) order.</summary>
    public static List<Tile> BuildOrdered()
    {
        var tiles = new List<Tile>(TotalTiles);
        int id = 0;
        foreach (TileColor color in Enum.GetValues<TileColor>())
        {
            for (int copy = 0; copy < 2; copy++)
                for (int number = 1; number <= 13; number++)
                    tiles.Add(Tile.Numbered(id++, color, number));
        }
        tiles.Add(Tile.Joker(id++));
        tiles.Add(Tile.Joker(id));
        return tiles;
    }

    /// <summary>Returns a freshly shuffled pool using the given RNG.</summary>
    public static List<Tile> BuildShuffled(Random rng)
    {
        var tiles = BuildOrdered();
        // Fisher–Yates.
        for (int i = tiles.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (tiles[i], tiles[j]) = (tiles[j], tiles[i]);
        }
        return tiles;
    }
}
