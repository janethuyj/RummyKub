using Microsoft.AspNetCore.SignalR;
using Rummikub.Server.Contracts;
using Rummikub.Server.Game;

namespace Rummikub.Server.Hubs;

/// <summary>
/// SignalR entry point. Deliberately thin: every method forwards to
/// <see cref="GameCoordinator"/>, which owns the game logic, broadcasting, timer
/// and AI. State is pushed back to clients via the "GameState" client method.
/// </summary>
public sealed class GameHub : Hub
{
    private readonly GameCoordinator _coordinator;

    public GameHub(GameCoordinator coordinator) => _coordinator = coordinator;

    public Task<JoinResult> CreateRoom(string playerName) =>
        _coordinator.CreateRoomAsync(Context.ConnectionId, playerName);

    public Task<JoinResult> JoinRoom(string code, string playerName) =>
        _coordinator.JoinRoomAsync(Context.ConnectionId, code, playerName);

    public Task<JoinResult> Rejoin(string code, string playerId) =>
        _coordinator.RejoinAsync(Context.ConnectionId, code, playerId);

    public Task<ActionResultDto> AddAiPlayer(string code) =>
        _coordinator.AddAiAsync(Context.ConnectionId, code);

    public Task<ActionResultDto> StartGame(string code) =>
        _coordinator.StartGameAsync(Context.ConnectionId, code);

    public Task<ActionResultDto> PlayAgain(string code) =>
        _coordinator.PlayAgainAsync(Context.ConnectionId, code);

    public Task PreviewMove(string code, List<List<int>> board) =>
        _coordinator.PreviewMoveAsync(Context.ConnectionId, code, board);

    public Task<ActionResultDto> DrawTile(string code) =>
        _coordinator.DrawAsync(Context.ConnectionId, code);

    public Task<ActionResultDto> CommitMove(string code, List<List<int>> board) =>
        _coordinator.CommitAsync(Context.ConnectionId, code, board);

    public Task<HintDto> RequestHint(string code) =>
        _coordinator.HintAsync(Context.ConnectionId, code);

    public Task<ActionResultDto> SetTimer(string code, int seconds) =>
        _coordinator.SetTimerAsync(Context.ConnectionId, code, seconds);

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _ = _coordinator.HandleDisconnectAsync(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
