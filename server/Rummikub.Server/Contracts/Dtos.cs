using Rummikub.Engine;

namespace Rummikub.Server.Contracts;

/// <summary>A tile as sent to the client. Colour is null for jokers.</summary>
public sealed record TileDto(int Id, string? Color, int Number, bool IsJoker)
{
    public static TileDto From(Tile t) =>
        new(t.Id, t.IsJoker ? null : t.Color.ToString().ToLowerInvariant(), t.IsJoker ? 0 : t.Number, t.IsJoker);
}

/// <summary>Public info about a player (never exposes another player's tiles).</summary>
public sealed record PlayerStateDto(
    string Id,
    string Name,
    bool IsAi,
    int RackCount,
    bool HasMelded,
    bool IsConnected,
    bool IsHost);

/// <summary>Full game state, personalized for one recipient (only their own rack is included).</summary>
public sealed record GameStateDto(
    string RoomCode,
    string Status,
    IReadOnlyList<PlayerStateDto> Players,
    IReadOnlyList<IReadOnlyList<TileDto>> Board,
    int CurrentPlayerIndex,
    string? CurrentPlayerId,
    string? WinnerId,
    int DrawPileCount,
    int TimerSeconds,
    long? TurnDeadlineUnixMs,
    string YourPlayerId,
    IReadOnlyList<TileDto> YourRack);

public sealed record JoinResult(bool Ok, string? Error, string? RoomCode, string? PlayerId);

public sealed record ActionResultDto(bool Ok, string? Error)
{
    public static ActionResultDto From(ActionResult r) => new(r.Ok, r.Error);
}

/// <summary>A hint for the human player: either draw, or a suggested set of tile ids to play.</summary>
public sealed record HintDto(bool ShouldDraw, IReadOnlyList<int> SuggestedTileIds, IReadOnlyList<IReadOnlyList<int>>? SuggestedBoard);
