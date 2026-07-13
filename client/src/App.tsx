import { useEffect } from 'react';
import { useStore } from './store';
import { Lobby } from './components/Lobby';
import { Room } from './components/Room';
import { ErrorToast } from './components/ErrorToast';

export function App() {
  const connect = useStore((s) => s.connect);
  const connected = useStore((s) => s.connected);
  const game = useStore((s) => s.game);

  useEffect(() => {
    void connect();
  }, [connect]);

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
