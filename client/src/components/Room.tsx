import { useStore } from '../store';
import { GameTable } from './GameTable';

/** Switches between the waiting-room lobby and the live game table. */
export function Room() {
  const game = useStore((s) => s.game)!;
  if (game.status === 'Lobby') return <WaitingRoom />;
  return <GameTable />;
}

function WaitingRoom() {
  const game = useStore((s) => s.game)!;
  const addAi = useStore((s) => s.addAi);
  const startGame = useStore((s) => s.startGame);
  const setTimer = useStore((s) => s.setTimer);
  const leave = useStore((s) => s.leave);

  const me = game.players.find((p) => p.id === game.yourPlayerId);
  const isHost = me?.isHost ?? false;
  const canStart = isHost && game.players.length >= 2;
  const full = game.players.length >= 4;

  return (
    <div className="card waiting-room">
      <div className="room-code-row">
        <span>Room code</span>
        <code className="room-code">{game.roomCode}</code>
        <button
          className="btn tiny"
          onClick={() => void navigator.clipboard?.writeText(game.roomCode)}
        >
          Copy
        </button>
      </div>

      <ul className="player-list">
        {game.players.map((p) => (
          <li key={p.id}>
            <span className="player-dot" />
            {p.name}
            {p.isAi && <span className="tag">AI</span>}
            {p.isHost && <span className="tag host">Host</span>}
            {p.id === game.yourPlayerId && <span className="tag you">You</span>}
          </li>
        ))}
        {Array.from({ length: 4 - game.players.length }).map((_, i) => (
          <li key={`empty-${i}`} className="empty-seat">
            Waiting for player…
          </li>
        ))}
      </ul>

      {isHost && (
        <div className="host-controls">
          <div className="timer-select">
            <span>Turn timer</span>
            {[0, 30, 60].map((s) => (
              <button
                key={s}
                className={`btn tiny ${game.timerSeconds === s ? 'active' : ''}`}
                onClick={() => void setTimer(s)}
              >
                {s === 0 ? 'Off' : `${s}s`}
              </button>
            ))}
          </div>

          <div className="host-buttons">
            <button className="btn" disabled={full} onClick={() => void addAi()}>
              Add AI player
            </button>
            <button className="btn primary" disabled={!canStart} onClick={() => void startGame()}>
              Start game
            </button>
          </div>
          {!canStart && <p className="hint-text">Need at least 2 players to start.</p>}
        </div>
      )}

      {!isHost && <p className="hint-text">Waiting for the host to start…</p>}

      <button className="btn text" onClick={leave}>
        Leave room
      </button>
    </div>
  );
}
