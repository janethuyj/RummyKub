import { useEffect, useState } from 'react';
import { useStore } from '../store';

/**
 * Renders the current turn's countdown from the server-provided deadline.
 * The server is authoritative (it auto-draws on timeout); this only displays
 * the remaining seconds, recomputed locally each second.
 */
export function TurnTimer() {
  const game = useStore((s) => s.game)!;
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 500);
    return () => clearInterval(id);
  }, []);

  if (game.timerSeconds <= 0 || game.turnDeadlineUnixMs == null) {
    return <span className="turn-timer off">No timer</span>;
  }

  const remainingMs = Math.max(0, game.turnDeadlineUnixMs - now);
  const seconds = Math.ceil(remainingMs / 1000);
  const urgent = seconds <= 10;

  return (
    <span className={`turn-timer ${urgent ? 'urgent' : ''}`}>
      ⏱ {seconds}s
    </span>
  );
}
