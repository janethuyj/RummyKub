using Rummikub.Engine;

namespace Rummikub.Engine.Tests;

public class EngineExtrasTests
{
    private static int _nextId = 9000;
    private static Tile N(TileColor c, int n) => Tile.Numbered(_nextId++, c, n);
    private static Tile J() => Tile.Joker(_nextId++);
    private static IReadOnlyList<IReadOnlyList<Tile>> EmptyBoard => Array.Empty<IReadOnlyList<Tile>>();

    // ---- SetFinder / RackOrganizer ----

    [Fact]
    public void SetFinder_FindsRunAndGroup()
    {
        var rack = new List<Tile>
        {
            N(TileColor.Red, 4), N(TileColor.Red, 5), N(TileColor.Red, 6),   // run
            N(TileColor.Blue, 9), N(TileColor.Black, 9), N(TileColor.Orange, 9), // group
            N(TileColor.Blue, 1) // loose
        };
        var result = SetFinder.FindDisjointSets(rack);
        Assert.Equal(2, result.Sets.Count);
        Assert.All(result.Sets, s => Assert.True(RuleValidator.IsValidSet(s)));
        Assert.Single(result.Leftovers);
    }

    [Fact]
    public void SetFinder_UsesJokerToCompleteNearSet()
    {
        var rack = new List<Tile> { N(TileColor.Red, 5), N(TileColor.Red, 7), J() };
        var result = SetFinder.FindDisjointSets(rack);
        Assert.Single(result.Sets);
        Assert.True(RuleValidator.IsValidSet(result.Sets[0]));
    }

    [Fact]
    public void RackOrganizer_KeepsAllTiles()
    {
        var rack = new List<Tile>
        {
            N(TileColor.Orange, 2), N(TileColor.Red, 4), N(TileColor.Red, 5),
            N(TileColor.Red, 6), N(TileColor.Blue, 12)
        };
        var organized = RackOrganizer.Organize(rack);
        Assert.Equal(rack.Count, organized.Ordered.Count);
        Assert.Equal(rack.Select(t => t.Id).OrderBy(x => x),
                     organized.Ordered.Select(t => t.Id).OrderBy(x => x));
    }

    // ---- OptimalMoveFinder / HintService ----

    [Fact]
    public void MoveFinder_PreMeld_PlaysWhenReaching30()
    {
        var rack = new List<Tile> { N(TileColor.Red, 10), N(TileColor.Blue, 10), N(TileColor.Black, 10) };
        var move = new OptimalMoveFinder().FindMove(EmptyBoard, rack, hasMelded: false);
        Assert.NotNull(move);
        Assert.Equal(3, move!.PlayedTileIds.Count);
    }

    [Fact]
    public void MoveFinder_PreMeld_DrawsWhenUnder30()
    {
        var rack = new List<Tile> { N(TileColor.Red, 1), N(TileColor.Blue, 1), N(TileColor.Black, 1) };
        var move = new OptimalMoveFinder().FindMove(EmptyBoard, rack, hasMelded: false);
        Assert.Null(move); // 3 points < 30 → draw
    }

    [Fact]
    public void MoveFinder_PostMeld_AttachesTileToExistingRun()
    {
        var existing = new List<Tile> { N(TileColor.Red, 4), N(TileColor.Red, 5), N(TileColor.Red, 6) };
        var board = new List<IReadOnlyList<Tile>> { existing };
        var rack = new List<Tile> { N(TileColor.Red, 7) };
        var move = new OptimalMoveFinder().FindMove(board, rack, hasMelded: true);
        Assert.NotNull(move);
        Assert.Contains(rack[0].Id, move!.PlayedTileIds);
    }

    [Fact]
    public void Hint_RecommendsDrawWhenNoMove()
    {
        var rack = new List<Tile> { N(TileColor.Red, 1), N(TileColor.Blue, 2), N(TileColor.Black, 8) };
        var hint = new HintService(new OptimalMoveFinder()).GetHint(EmptyBoard, rack, hasMelded: false);
        Assert.True(hint.ShouldDraw);
    }

    // ---- TurnHistory ----

    [Fact]
    public void TurnHistory_UndoOne_RestoresPreviousState()
    {
        var history = new TurnHistory();
        var rack0 = new List<Tile> { N(TileColor.Red, 5), N(TileColor.Red, 6), N(TileColor.Red, 7) };
        history.Begin(EmptyBoard, rack0);

        var rack1 = new List<Tile> { rack0[2] };
        var board1 = new List<IReadOnlyList<Tile>> { new List<Tile> { rack0[0], rack0[1] } };
        history.Record(board1, rack1);

        Assert.True(history.CanUndo);
        var afterUndo = history.Undo();
        Assert.Equal(3, afterUndo.Rack.Count);
        Assert.Empty(afterUndo.Board);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void TurnHistory_UndoAll_ReturnsToStart()
    {
        var history = new TurnHistory();
        var rack0 = new List<Tile> { N(TileColor.Blue, 1), N(TileColor.Blue, 2), N(TileColor.Blue, 3) };
        history.Begin(EmptyBoard, rack0);
        history.Record(new List<IReadOnlyList<Tile>> { new List<Tile> { rack0[0] } }, new List<Tile> { rack0[1], rack0[2] });
        history.Record(new List<IReadOnlyList<Tile>> { new List<Tile> { rack0[0], rack0[1] } }, new List<Tile> { rack0[2] });

        var start = history.UndoAll();
        Assert.Equal(3, start.Rack.Count);
        Assert.Empty(start.Board);
        Assert.False(history.CanUndo);
    }
}
