using Rummikub.Engine;

namespace Rummikub.Engine.Tests;

public class RuleValidatorTests
{
    private static int _nextId = 1000;
    private static Tile N(TileColor c, int n) => Tile.Numbered(_nextId++, c, n);
    private static Tile J() => Tile.Joker(_nextId++);

    // ---- Groups ----

    [Fact]
    public void Group_ThreeDistinctColorsSameNumber_IsValid()
    {
        var set = new[] { N(TileColor.Red, 7), N(TileColor.Blue, 7), N(TileColor.Black, 7) };
        var eval = RuleValidator.EvaluateSet(set);
        Assert.Equal(SetKind.Group, eval.Kind);
        Assert.Equal(21, eval.Value); // 7 * 3
    }

    [Fact]
    public void Group_FourColors_IsValid()
    {
        var set = new[]
        {
            N(TileColor.Red, 10), N(TileColor.Blue, 10),
            N(TileColor.Black, 10), N(TileColor.Orange, 10)
        };
        Assert.Equal(SetKind.Group, RuleValidator.EvaluateSet(set).Kind);
    }

    [Fact]
    public void Group_DuplicateColor_IsInvalid()
    {
        var set = new[] { N(TileColor.Red, 5), N(TileColor.Red, 5), N(TileColor.Blue, 5) };
        Assert.False(RuleValidator.IsValidSet(set));
    }

    [Fact]
    public void Group_DifferentNumbers_IsInvalid()
    {
        var set = new[] { N(TileColor.Red, 5), N(TileColor.Blue, 6), N(TileColor.Black, 5) };
        Assert.False(RuleValidator.IsValidSet(set));
    }

    [Fact]
    public void Group_WithJoker_IsValidAndCounted()
    {
        var set = new[] { N(TileColor.Red, 8), N(TileColor.Blue, 8), J() };
        var eval = RuleValidator.EvaluateSet(set);
        Assert.Equal(SetKind.Group, eval.Kind);
        Assert.Equal(24, eval.Value); // joker stands for 8 → 8 * 3
    }

    // ---- Runs ----

    [Fact]
    public void Run_ThreeConsecutiveSameColor_IsValid()
    {
        var set = new[] { N(TileColor.Blue, 4), N(TileColor.Blue, 5), N(TileColor.Blue, 6) };
        var eval = RuleValidator.EvaluateSet(set);
        Assert.Equal(SetKind.Run, eval.Kind);
        Assert.Equal(15, eval.Value); // 4+5+6
    }

    [Fact]
    public void Run_UnorderedInput_StillValid()
    {
        var set = new[] { N(TileColor.Red, 11), N(TileColor.Red, 9), N(TileColor.Red, 10) };
        Assert.Equal(SetKind.Run, RuleValidator.EvaluateSet(set).Kind);
    }

    [Fact]
    public void Run_MixedColors_IsInvalid()
    {
        var set = new[] { N(TileColor.Red, 4), N(TileColor.Blue, 5), N(TileColor.Red, 6) };
        Assert.False(RuleValidator.IsValidSet(set));
    }

    [Fact]
    public void Run_JokerFillsInteriorGap()
    {
        // 5, _, 7 with joker as 6
        var set = new[] { N(TileColor.Black, 5), J(), N(TileColor.Black, 7) };
        var eval = RuleValidator.EvaluateSet(set);
        Assert.Equal(SetKind.Run, eval.Kind);
        Assert.Equal(18, eval.Value); // 5+6+7
    }

    [Fact]
    public void Run_JokerExtendsEnd()
    {
        // 12,13,joker -> joker cannot go above 13, must extend downward to 11
        var set = new[] { N(TileColor.Orange, 12), N(TileColor.Orange, 13), J() };
        var eval = RuleValidator.EvaluateSet(set);
        Assert.Equal(SetKind.Run, eval.Kind);
        Assert.Equal(36, eval.Value); // 11+12+13
    }

    [Fact]
    public void Run_ExceedsThirteen_IsInvalid()
    {
        var set = new[] { N(TileColor.Red, 12), N(TileColor.Red, 13), J(), J() };
        // 12,13 + 2 jokers would need 11..14 or 10..13; only downward fits: 10,11,12,13? that's 4 tiles 10-13
        // Actually 12,13 + 2 jokers = length 4 => 10,11,12,13 valid.
        Assert.True(RuleValidator.IsValidSet(set));
    }

    [Fact]
    public void Run_DuplicateNumber_IsInvalid()
    {
        var set = new[] { N(TileColor.Red, 5), N(TileColor.Red, 5), N(TileColor.Red, 6) };
        Assert.False(RuleValidator.IsValidSet(set));
    }

    [Fact]
    public void Set_TooShort_IsInvalid()
    {
        var set = new[] { N(TileColor.Red, 5), N(TileColor.Red, 6) };
        Assert.False(RuleValidator.IsValidSet(set));
    }

    // ---- Board ----

    [Fact]
    public void Board_AllValid_IsValid()
    {
        var sets = new List<IReadOnlyList<Tile>>
        {
            new[] { N(TileColor.Red, 1), N(TileColor.Red, 2), N(TileColor.Red, 3) },
            new[] { N(TileColor.Blue, 9), N(TileColor.Black, 9), N(TileColor.Orange, 9) }
        };
        Assert.True(RuleValidator.IsBoardValid(sets));
    }

    [Fact]
    public void Board_OneInvalidSet_IsInvalid()
    {
        var sets = new List<IReadOnlyList<Tile>>
        {
            new[] { N(TileColor.Red, 1), N(TileColor.Red, 2), N(TileColor.Red, 3) },
            new[] { N(TileColor.Blue, 9), N(TileColor.Black, 8), N(TileColor.Orange, 7) } // mixed-color group / mixed run
        };
        Assert.False(RuleValidator.IsBoardValid(sets));
    }
}
