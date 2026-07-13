namespace Rummikub.Engine;

/// <summary>A proposed play: the full board after the move, plus which tile ids were played from the rack.</summary>
public sealed record ProposedMove(List<List<Tile>> Board, List<int> PlayedTileIds)
{
    public bool PlaysAnything => PlayedTileIds.Count > 0;
}

/// <summary>
/// Strategy for choosing a move. The greedy implementation ships first; an optimal
/// Den Hertog–Hulshof solver can be dropped in later without changing callers.
/// Returns <c>null</c> when the finder recommends drawing a tile instead of playing.
/// </summary>
public interface IMoveFinder
{
    ProposedMove? FindMove(
        IReadOnlyList<IReadOnlyList<Tile>> board,
        IReadOnlyList<Tile> rack,
        bool hasMelded);
}
