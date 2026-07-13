namespace Rummikub.Engine;

/// <summary>An immutable snapshot of the working board + rack during a turn.</summary>
public sealed record TurnSnapshot(
    IReadOnlyList<IReadOnlyList<Tile>> Board,
    IReadOnlyList<Tile> Rack);

/// <summary>
/// Tracks the sequence of states a player moves through during their turn so they
/// can <see cref="Undo"/> one step or <see cref="UndoAll"/> back to the start of the
/// turn. The base snapshot (start of turn) can never be popped. A new turn calls
/// <see cref="Begin"/> to reset the stack.
/// </summary>
public sealed class TurnHistory
{
    private readonly List<TurnSnapshot> _stack = new();

    /// <summary>Starts a new turn from the given board/rack; clears prior history.</summary>
    public void Begin(IReadOnlyList<IReadOnlyList<Tile>> board, IReadOnlyList<Tile> rack)
    {
        _stack.Clear();
        _stack.Add(Snapshot(board, rack));
    }

    /// <summary>Records a new intermediate state after the player moves a tile.</summary>
    public void Record(IReadOnlyList<IReadOnlyList<Tile>> board, IReadOnlyList<Tile> rack)
    {
        if (_stack.Count == 0)
            throw new InvalidOperationException("Call Begin() before Record().");
        _stack.Add(Snapshot(board, rack));
    }

    public bool CanUndo => _stack.Count > 1;

    public TurnSnapshot Current => _stack.Count > 0
        ? _stack[^1]
        : throw new InvalidOperationException("No turn in progress.");

    /// <summary>Undoes one step; returns the now-current snapshot. No-op at the base.</summary>
    public TurnSnapshot Undo()
    {
        if (CanUndo)
            _stack.RemoveAt(_stack.Count - 1);
        return Current;
    }

    /// <summary>Resets to the start-of-turn snapshot; returns it.</summary>
    public TurnSnapshot UndoAll()
    {
        var baseSnapshot = _stack[0];
        _stack.Clear();
        _stack.Add(baseSnapshot);
        return baseSnapshot;
    }

    private static TurnSnapshot Snapshot(IReadOnlyList<IReadOnlyList<Tile>> board, IReadOnlyList<Tile> rack)
        => new(board.Select(s => (IReadOnlyList<Tile>)s.ToList()).ToList(), rack.ToList());
}
