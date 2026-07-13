using Rummikub.Engine;

namespace Rummikub.Engine.Tests;

public class TurnValidatorTests
{
    private static int _nextId = 5000;
    private static Tile N(TileColor c, int n) => Tile.Numbered(_nextId++, c, n);
    private static Tile J() => Tile.Joker(_nextId++);

    private static IReadOnlyList<IReadOnlyList<Tile>> Board(params IReadOnlyList<Tile>[] sets) => sets;

    [Fact]
    public void InitialMeld_AtLeast30_FromNewSets_IsValid()
    {
        var rack = new List<Tile> { N(TileColor.Red, 10), N(TileColor.Blue, 10), N(TileColor.Black, 10) };
        var proposed = Board(new[] { rack[0], rack[1], rack[2] }); // group of 10s = 30
        var result = TurnValidator.Validate(Board(), rack, proposed, hasMeldedBefore: false);
        Assert.True(result.Ok, result.Error);
    }

    [Fact]
    public void InitialMeld_Below30_IsRejected()
    {
        var rack = new List<Tile> { N(TileColor.Red, 3), N(TileColor.Blue, 3), N(TileColor.Black, 3) };
        var proposed = Board(new[] { rack[0], rack[1], rack[2] }); // 9 points
        var result = TurnValidator.Validate(Board(), rack, proposed, hasMeldedBefore: false);
        Assert.False(result.Ok);
        Assert.Contains("30", result.Error);
    }

    [Fact]
    public void PreMeld_TouchingExistingBoard_IsRejected()
    {
        var existing = new[] { N(TileColor.Red, 1), N(TileColor.Red, 2), N(TileColor.Red, 3) };
        var start = Board(existing);
        var rackTile = N(TileColor.Red, 4);
        var rack = new List<Tile> { rackTile };
        // Player tries to extend the existing run before melding.
        var proposed = Board(new[] { existing[0], existing[1], existing[2], rackTile });
        var result = TurnValidator.Validate(start, rack, proposed, hasMeldedBefore: false);
        Assert.False(result.Ok);
    }

    [Fact]
    public void PostMeld_RearrangingBoard_IsValid()
    {
        // Existing run 4-5-6 red; player adds red 7 from rack and forms 4-5-6-7.
        var existing = new[] { N(TileColor.Red, 4), N(TileColor.Red, 5), N(TileColor.Red, 6) };
        var start = Board(existing);
        var seven = N(TileColor.Red, 7);
        var rack = new List<Tile> { seven };
        var proposed = Board(new[] { existing[0], existing[1], existing[2], seven });
        var result = TurnValidator.Validate(start, rack, proposed, hasMeldedBefore: true);
        Assert.True(result.Ok, result.Error);
    }

    [Fact]
    public void UsingTileNotInRack_IsRejected()
    {
        var rack = new List<Tile> { N(TileColor.Red, 10), N(TileColor.Blue, 10) };
        var stranger = N(TileColor.Black, 10); // not in rack, not on board
        var proposed = Board(new[] { rack[0], rack[1], stranger });
        var result = TurnValidator.Validate(Board(), rack, proposed, hasMeldedBefore: true);
        Assert.False(result.Ok);
    }

    [Fact]
    public void RemovingBoardTile_IsRejected()
    {
        var existing = new[] { N(TileColor.Red, 4), N(TileColor.Red, 5), N(TileColor.Red, 6) };
        var start = Board(existing);
        var rack = new List<Tile> { N(TileColor.Blue, 9) };
        // Proposed board drops one existing tile — illegal.
        var proposed = Board(new[] { existing[0], existing[1] });
        var result = TurnValidator.Validate(start, rack, proposed, hasMeldedBefore: true);
        Assert.False(result.Ok);
    }

    [Fact]
    public void NoTilePlaced_IsRejected()
    {
        var existing = new[] { N(TileColor.Red, 4), N(TileColor.Red, 5), N(TileColor.Red, 6) };
        var start = Board(existing);
        var rack = new List<Tile> { N(TileColor.Blue, 9) };
        var proposed = Board(existing); // unchanged
        var result = TurnValidator.Validate(start, rack, proposed, hasMeldedBefore: true);
        Assert.False(result.Ok);
    }

    [Fact]
    public void InvalidSetOnBoard_IsRejected()
    {
        var rack = new List<Tile> { N(TileColor.Red, 10), N(TileColor.Blue, 9), N(TileColor.Black, 8) };
        var proposed = Board(new[] { rack[0], rack[1], rack[2] }); // not a group, not a run
        var result = TurnValidator.Validate(Board(), rack, proposed, hasMeldedBefore: true);
        Assert.False(result.Ok);
    }
}
