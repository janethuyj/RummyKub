import { useEffect } from 'react';
import { useStore } from './store';
import { Lobby } from './components/Lobby';
import { Room } from './components/Room';
import { ErrorToast } from './components/ErrorToast';
import { unlockAudio } from './sound';

export function App() {
  const connect = useStore((s) => s.connect);
  const connected = useStore((s) => s.connected);
  const game = useStore((s) => s.game);

  useEffect(() => {
    void connect();
  }, [connect]);

  useEffect(() => {
    // Browsers require a user gesture before audio can play; unlock on first tap.
    const unlock = () => unlockAudio();
    window.addEventListener('pointerdown', unlock, { once: true });
    return () => window.removeEventListener('pointerdown', unlock);
  }, []);

  return (
    <div className="app">
      <header className="app-header">
        <span className="logo">🁢 RummyKub</span>
      </header>
      <main className="app-main">
        {!connected ? (
          <p className="status-line">Connecting…</p>
        ) : game ? (
          <Room />
        ) : (
          <Lobby />
        )}
      </main>
      <ErrorToast />
    </div>
  );
}
