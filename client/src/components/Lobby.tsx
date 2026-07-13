import { useState } from 'react';
import { useStore } from '../store';

/** Landing screen: enter a name, then create a room or join one by code. */
export function Lobby() {
  const createRoom = useStore((s) => s.createRoom);
  const joinRoom = useStore((s) => s.joinRoom);
  const [name, setName] = useState('');
  const [code, setCode] = useState('');

  const trimmedName = name.trim() || 'Player';

  return (
    <div className="lobby card">
      <h1>Play RummyKub</h1>
      <label className="field">
        <span>Your name</span>
        <input
          value={name}
          maxLength={20}
          placeholder="e.g. Alex"
          onChange={(e) => setName(e.target.value)}
        />
      </label>

      <div className="lobby-actions">
        <button className="btn primary" onClick={() => void createRoom(trimmedName)}>
          Create a room
        </button>

        <div className="join-row">
          <input
            className="code-input"
            value={code}
            maxLength={4}
            placeholder="CODE"
            onChange={(e) => setCode(e.target.value.toUpperCase())}
          />
          <button
            className="btn"
            disabled={code.trim().length < 4}
            onClick={() => void joinRoom(code, trimmedName)}
          >
            Join
          </button>
        </div>
      </div>

      <p className="hint-text">
        Create a room and share the 4-letter code, or add AI opponents to play offline.
      </p>
    </div>
  );
}
