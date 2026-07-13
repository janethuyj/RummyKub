namespace Rummikub.Engine;

/// <summary>
/// Produces a hint for a human player by reusing the same move finder the AI uses.
/// The hint is a suggested play (or a recommendation to draw). The UI decides
/// whether to show it — the on/off toggle simply enables the button.
/// </summary>
public sealed class HintService
{
    private readonly IMoveFinder _finder;

    public HintService(IMoveFinder finder) => _finder = finder;

    public sealed record Hint(bool ShouldDraw, ProposedMove? Move, IReadOnlyList<int> SuggestedTileIds);

    public Hint GetHint(
        IReadOnlyList<IReadOnlyList<Tile>> board,
        IReadOnlyList<Tile> rack,
        bool hasMelded)
    {
        var move = _finder.FindMove(board, rack, hasMelded);
        if (move is null || !move.PlaysAnything)
            return new Hint(ShouldDraw: true, Move: null, SuggestedTileIds: Array.Empty<int>());

        return new Hint(ShouldDraw: false, Move: move, SuggestedTileIds: move.PlayedTileIds);
    }
}
