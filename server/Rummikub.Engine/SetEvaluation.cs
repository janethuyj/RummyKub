namespace Rummikub.Engine;

public enum SetKind
{
    Invalid,
    Group,
    Run
}

/// <summary>
/// Result of validating a candidate set of tiles. <see cref="Value"/> is the sum
/// of the tile values (jokers counted as the tile they represent) and is used for
/// the 30-point initial-meld check. Zero when the set is invalid.
/// </summary>
public readonly record struct SetEvaluation(SetKind Kind, int Value)
{
    public bool IsValid => Kind != SetKind.Invalid;

    public static readonly SetEvaluation Invalid = new(SetKind.Invalid, 0);
}
