using Rummikub.Engine;

namespace Rummikub.Engine.Tests;

public class GameSessionTests
{
    private static GameSession TwoPlayerGame(int seed = 42)
    {
        var game = new GameSession();
        game.AddPlayer("p1", "Alice", isAi: false);
        game.AddPlayer("p2", "Bob", isAi: false);
        game.Start(new Random(seed));
        return game;
    }

    [Fact]
    public void Restart_WhileStillPlaying_IsRejected()
    {
        var game = TwoPlayerGame(); // status Playing
        Assert.False(game.Restart(new Random(1)).Ok);
    }

    [Fact]
    public void Restart_AfterFinish_DealsAgainAndResetsMeld()
    {
        var game = TwoPlayerGame();
        var p1 = game.Players.First(p => p.Id == "p1"); // p1 goes first

        // Give p1 exactly one valid 30-point set so committing it empties the rack and wins.
        p1.Rack.Clear();
        p1.Rack.Add(Tile.Numbered(200, TileColor.Blue, 12));
        p1.Rack.Add(Tile.Numbered(201, TileColor.Red, 12));
        p1.Rack.Add(Tile.Numbered(202, TileColor.Black, 12));

        var win = game.CommitMove("p1", new List<IReadOnlyList<int>> { new List<int> { 200, 201, 202 } });
        Assert.True(win.Ok, win.Error);
        Assert.Equal(GameStatus.Finished, game.Status);
        Assert.Equal("p1", game.WinnerId);

        var restart = game.Restart(new Random(7));
        Assert.True(restart.Ok, restart.Error);
        Assert.Equal(GameStatus.Playing, game.Status);
        Assert.Null(game.WinnerId);
        Assert.All(game.Players, p => Assert.Equal(GameSession.StartingRackSize, p.Rack.Count));
        Assert.All(game.Players, p => Assert.False(p.HasMadeInitialMeld));
    }

    [Fact]
    public void EmptyPool_AllPass_EndsWithLowestHandWinner()
    {
        var game = new GameSession();
        game.AddPlayer("p1", "Alice", false);
        game.AddPlayer("p2", "Bob", false);
        game.Start(new Random(3));

        // Drain the pool by drawing and passing until it is empty.
        int guard = 0;
        while (game.DrawPileCount > 0 && guard++ < 200)
            game.DrawAndPass(game.CurrentPlayer!.Id);

        Assert.Equal(GameStatus.Playing, game.Status); // still going, pool just emptied

        // Now every player forced-passes; after a full round the game ends.
        game.DrawAndPass(game.CurrentPlayer!.Id);
        game.DrawAndPass(game.CurrentPlayer!.Id);

        Assert.Equal(GameStatus.Finished, game.Status);
        Assert.NotNull(game.WinnerId); // lowest-hand player wins
    }

    [Fact]
    public void Start_DealsFourteenTilesEach()
    {
        var game = TwoPlayerGame();
        Assert.Equal(GameStatus.Playing, game.Status);
        Assert.All(game.Players, p => Assert.Equal(GameSession.StartingRackSize, p.Rack.Count));
        Assert.Equal(TilePool.TotalTiles - 2 * GameSession.StartingRackSize, game.DrawPileCount);
    }

    [Fact]
    public void AddPlayer_BeyondFour_IsRejected()
    {
        var game = new GameSession();
        Assert.True(game.AddPlayer("1", "A", false).Ok);
        Assert.True(game.AddPlayer("2", "B", false).Ok);
        Assert.True(game.AddPlayer("3", "C", false).Ok);
        Assert.True(game.AddPlayer("4", "D", false).Ok);
        Assert.False(game.AddPlayer("5", "E", false).Ok);
    }

    [Fact]
    public void Start_WithOnePlayer_IsRejected()
    {
        var game = new GameSession();
        game.AddPlayer("1", "A", false);
        Assert.False(game.Start(new Random(1)).Ok);
    }

    [Fact]
    public void DrawAndPass_GrowsRackAndAdvancesTurn()
    {
        var game = TwoPlayerGame();
        var first = game.CurrentPlayer!;
        int before = first.Rack.Count;

        var result = game.DrawAndPass(first.Id);

        Assert.True(result.Ok);
        Assert.Equal(before + 1, first.Rack.Count);
        Assert.NotEqual(first.Id, game.CurrentPlayer!.Id); // turn advanced
    }

    [Fact]
    public void Action_OutOfTurn_IsRejected()
    {
        var game = TwoPlayerGame();
        var notCurrent = game.Players.First(p => p.Id != game.CurrentPlayer!.Id);
        Assert.False(game.DrawAndPass(notCurrent.Id).Ok);
    }

    [Fact]
    public void CommitMove_WithForeignTile_IsRejected()
    {
        var game = TwoPlayerGame();
        var current = game.CurrentPlayer!;
        // 999999 is not any real tile id.
        var proposed = new List<IReadOnlyList<int>> { new List<int> { 999999, 999998, 999997 } };
        Assert.False(game.CommitMove(current.Id, proposed).Ok);
    }
}
