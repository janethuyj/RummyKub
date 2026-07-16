using System.Diagnostics;
using Rummikub.Engine;

namespace Rummikub.Engine.Tests;

/// <summary>
/// Covers what separates the solver from a greedy chooser: rearranging the board to
/// make room, decomposing a rack the best way rather than the most obvious way, and
/// spending jokers only when they pay. Every test asserts through the rules engine.
/// </summary>
public class OptimalMoveFinderTests
{
    private static int _nextId = 1;
    private static Tile N(TileColor c, int n) => Tile.Numbered(_nextId++, c, n);
    private static Tile J() => Tile.Joker(_nextId++);
    private static IReadOnlyList<IReadOnlyList<Tile>> EmptyBoard => Array.Empty<IReadOnlyList<Tile>>();

    private static readonly OptimalMoveFinder Finder = new();

    private static int TilesPlayed(ProposedMove? move) => move?.PlayedTileIds.Count ?? 0;

    // ---- Board manipulation ----

    [Fact]
    public void BorrowsTileFromBoardRunToFormGroup()
    {
        // R4-R5-R6-R7 on the board, B7 and K7 on the rack. Neither plays on its own, but
        // taking R7 off the run (which stays valid at R4-R5-R6) makes the group 7-7-7.
        var board = new List<IReadOnlyList<Tile>>
        {
            new List<Tile> { N(TileColor.Red, 4), N(TileColor.Red, 5), N(TileColor.Red, 6), N(TileColor.Red, 7) },
        };
        var rack = new List<Tile> { N(TileColor.Blue, 7), N(TileColor.Black, 7) };

        var move = Finder.FindMove(board, rack, hasMelded: true);

        Assert.Equal(2, TilesPlayed(move));
        AssertLegal(board, rack, move, hasMelded: true);
    }

    [Fact]
    public void SplitsLongRunToPlaceDuplicateTile()
    {
        // R1..R7 on the board and a second R4 on the rack: the only way to place it is to
        // split the run into R1-R2-R3-R4 and R4-R5-R6-R7.
        var board = new List<IReadOnlyList<Tile>>
        {
            new List<Tile>
            {
                N(TileColor.Red, 1), N(TileColor.Red, 2), N(TileColor.Red, 3), N(TileColor.Red, 4),
                N(TileColor.Red, 5), N(TileColor.Red, 6), N(TileColor.Red, 7),
            },
        };
        var rack = new List<Tile> { N(TileColor.Red, 4) };

        var move = Finder.FindMove(board, rack, hasMelded: true);

        Assert.Equal(1, TilesPlayed(move));
        AssertLegal(board, rack, move, hasMelded: true);
    }

    // ---- Decomposition ----

    [Fact]
    public void PrefersTwoSetsOverOneLargerSet()
    {
        // Grabbing the biggest set first takes the four 1s and strands R2-R3. Splitting the
        // 1s into a group of three instead leaves R1 to carry the run, playing all six tiles.
        var rack = new List<Tile>
        {
            N(TileColor.Red, 1), N(TileColor.Red, 2), N(TileColor.Red, 3),
            N(TileColor.Blue, 1), N(TileColor.Black, 1), N(TileColor.Orange, 1),
        };

        var move = Finder.FindMove(EmptyBoard, rack, hasMelded: true);

        Assert.Equal(6, TilesPlayed(move));
        AssertLegal(EmptyBoard, rack, move, hasMelded: true);
    }

    [Fact]
    public void InitialMeldPlaysEverythingItCan()
    {
        // The run alone clears 30, but there is no reason to hold the group back.
        var rack = new List<Tile>
        {
            N(TileColor.Red, 10), N(TileColor.Red, 11), N(TileColor.Red, 12), N(TileColor.Red, 13),
            N(TileColor.Blue, 10), N(TileColor.Black, 10), N(TileColor.Orange, 10),
        };

        var move = Finder.FindMove(EmptyBoard, rack, hasMelded: false);

        Assert.Equal(7, TilesPlayed(move));
        AssertLegal(EmptyBoard, rack, move, hasMelded: false);
    }

    [Fact]
    public void InitialMeldIgnoresBoardAndDrawsWhenShort()
    {
        // R4-R5-R6 is on the board and R7 would extend it — but not before melding.
        var board = new List<IReadOnlyList<Tile>>
        {
            new List<Tile> { N(TileColor.Red, 4), N(TileColor.Red, 5), N(TileColor.Red, 6) },
        };
        var rack = new List<Tile> { N(TileColor.Red, 7) };

        Assert.Null(Finder.FindMove(board, rack, hasMelded: false));
    }

    // ---- Jokers ----

    [Fact]
    public void UsesJokerToCompleteASet()
    {
        var rack = new List<Tile> { N(TileColor.Red, 11), N(TileColor.Red, 13), J() };

        var move = Finder.FindMove(EmptyBoard, rack, hasMelded: false);

        Assert.Equal(3, TilesPlayed(move)); // R11 - joker as R12 - R13 = 36 points
        AssertLegal(EmptyBoard, rack, move, hasMelded: false);
    }

    [Fact]
    public void KeepsJokerWhenItOnlyPadsASet()
    {
        // The group of 9s stands on its own. Padding it out to 9-9-9-joker would play one more
        // tile, but a joker can stand in for anything later, so it is not worth one tile now.
        // R1/K13 are unplayable junk, so going out is off the table either way.
        var joker = J();
        var rack = new List<Tile>
        {
            N(TileColor.Red, 9), N(TileColor.Blue, 9), N(TileColor.Black, 9), joker,
            N(TileColor.Red, 1), N(TileColor.Black, 13),
        };

        var move = Finder.FindMove(EmptyBoard, rack, hasMelded: true);

        Assert.Equal(3, TilesPlayed(move));
        AssertLegal(EmptyBoard, rack, move, hasMelded: true);
        Assert.DoesNotContain(joker.Id, move!.PlayedTileIds);
    }

    [Fact]
    public void SpendsJokerWhenItFreesTwoStuckTiles()
    {
        // R11 and R13 are going nowhere without the joker bridging them at R12. Two tiles
        // unstuck is worth the joker; the junk keeps this from being a going-out play.
        var joker = J();
        var rack = new List<Tile>
        {
            N(TileColor.Red, 11), N(TileColor.Red, 13), joker,
            N(TileColor.Blue, 1), N(TileColor.Black, 4),
        };

        var move = Finder.FindMove(EmptyBoard, rack, hasMelded: true);

        Assert.Equal(3, TilesPlayed(move));
        AssertLegal(EmptyBoard, rack, move, hasMelded: true);
        Assert.Contains(joker.Id, move!.PlayedTileIds);
    }

    [Fact]
    public void SpendsJokerToGoOut()
    {
        // Hoarding a joker is pointless when playing it empties the rack and wins the round.
        var joker = J();
        var rack = new List<Tile> { N(TileColor.Red, 9), N(TileColor.Blue, 9), N(TileColor.Black, 9), joker };

        var move = Finder.FindMove(EmptyBoard, rack, hasMelded: true);

        Assert.Equal(4, TilesPlayed(move));
        AssertLegal(EmptyBoard, rack, move, hasMelded: true);
        Assert.Contains(joker.Id, move!.PlayedTileIds);
    }

    [Fact]
    public void LeavesBoardJokerInPlayWhenRebuilding()
    {
        // The board joker stands in for R6. Adding B7/K7 pulls R7 into a group, and the
        // joker has to stay somewhere legal on the board throughout.
        var joker = J();
        var board = new List<IReadOnlyList<Tile>>
        {
            new List<Tile> { N(TileColor.Red, 4), N(TileColor.Red, 5), joker, N(TileColor.Red, 7) },
        };
        var rack = new List<Tile> { N(TileColor.Blue, 7), N(TileColor.Black, 7) };

        var move = Finder.FindMove(board, rack, hasMelded: true);

        Assert.NotNull(move);
        AssertLegal(board, rack, move, hasMelded: true);
        Assert.Contains(joker.Id, move!.Board.SelectMany(s => s).Select(t => t.Id));
    }

    // ---- Nothing to do ----

    [Fact]
    public void DrawsWhenNothingPlays()
    {
        var board = new List<IReadOnlyList<Tile>>
        {
            new List<Tile> { N(TileColor.Red, 4), N(TileColor.Red, 5), N(TileColor.Red, 6) },
        };
        var rack = new List<Tile> { N(TileColor.Blue, 1), N(TileColor.Black, 9) };

        Assert.Null(Finder.FindMove(board, rack, hasMelded: true));
    }

    // ---- Whole-game fuzz ----

    [Fact]
    public void SelfPlay_EveryProposedMoveIsLegal_AndFast()
    {
        var clock = Stopwatch.StartNew();
        int slowest = 0;

        for (int seed = 0; seed < 25; seed++)
        {
            var rng = new Random(seed);
            var pool = new Queue<Tile>(BuildDeck().OrderBy(_ => rng.Next()));
            var racks = new[] { Draw(pool, 14), Draw(pool, 14) };
            var melded = new bool[2];
            var board = (IReadOnlyList<IReadOnlyList<Tile>>)new List<IReadOnlyList<Tile>>();

            for (int turn = 0; turn < 80; turn++)
            {
                int player = turn % 2;

                var timer = Stopwatch.StartNew();
                var move = Finder.FindMove(board, racks[player], melded[player]);
                slowest = Math.Max(slowest, (int)timer.ElapsedMilliseconds);

                if (move is null)
                {
                    if (pool.Count > 0) racks[player].Add(pool.Dequeue());
                    continue;
                }

                var check = TurnValidator.Validate(board, racks[player], move.Board, melded[player]);
                Assert.True(check.Ok, $"seed {seed} turn {turn}: {check.Error}");

                var played = move.PlayedTileIds.ToHashSet();
                racks[player].RemoveAll(t => played.Contains(t.Id));
                board = move.Board;
                melded[player] = true;

                if (racks[player].Count == 0) break;
            }
        }

        // The AI pauses ~900ms to look like it is thinking; the search must fit well inside that.
        Assert.True(slowest < 250, $"slowest single move took {slowest}ms");
        Assert.True(clock.ElapsedMilliseconds < 20_000, $"self-play took {clock.ElapsedMilliseconds}ms");
    }

    private static List<Tile> BuildDeck()
    {
        var deck = new List<Tile>();
        for (int copy = 0; copy < 2; copy++)
            foreach (TileColor color in Enum.GetValues<TileColor>())
                for (int number = 1; number <= 13; number++)
                    deck.Add(N(color, number));
        deck.Add(J());
        deck.Add(J());
        return deck;
    }

    private static List<Tile> Draw(Queue<Tile> pool, int count)
    {
        var hand = new List<Tile>();
        for (int i = 0; i < count && pool.Count > 0; i++)
            hand.Add(pool.Dequeue());
        return hand;
    }

    private static void AssertLegal(
        IReadOnlyList<IReadOnlyList<Tile>> board,
        IReadOnlyList<Tile> rack,
        ProposedMove? move,
        bool hasMelded)
    {
        Assert.NotNull(move);
        var check = TurnValidator.Validate(board, rack, move!.Board, hasMelded);
        Assert.True(check.Ok, check.Error);
    }
}
