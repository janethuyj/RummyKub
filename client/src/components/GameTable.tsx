import {
  closestCenter,
  DndContext,
  DragEndEvent,
  PointerSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
} from '@dnd-kit/core';
import type { Tile } from '../types';
import { isMyTurn, useStore } from '../store';
import { TileView } from './TileView';
import { TurnTimer } from './TurnTimer';

export function GameTable() {
  const game = useStore((s) => s.game)!;
  const working = useStore((s) => s.working);
  const moveTile = useStore((s) => s.moveTile);
  const hintTileIds = useStore((s) => s.hintTileIds);

  const myTurn = isMyTurn(game);
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 6 } }));

  function handleDragEnd(e: DragEndEvent) {
    if (!e.over) return;
    const tileId = Number(e.active.id);
    const target = String(e.over.id);
    if (target === 'rack') moveTile(tileId, 'rack');
    else if (target === 'board') moveTile(tileId, 'new'); // dropped on empty board area → new set
    else if (target.startsWith('set-')) moveTile(tileId, { setIndex: Number(target.slice(4)) });
  }

  const board = working?.board ?? game.board;
  const rack = working?.rack ?? game.yourRack;
  const hintSet = new Set(hintTileIds);

  return (
    <div className="game-table">
      <TopBar />

      <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={handleDragEnd}>
        <DropZone id="board" className="board" aria-label="Board">
          {board.length === 0 && (
            <p className="board-empty">
              {myTurn ? 'Drag tiles here to form a set.' : 'No sets on the board yet.'}
            </p>
          )}
          {board.map((set, i) => (
            <DropZone key={i} id={`set-${i}`} className="set">
              {set.map((t) => (
                <DraggableTile key={t.id} tile={t} disabled={!myTurn} highlighted={hintSet.has(t.id)} />
              ))}
            </DropZone>
          ))}
          {myTurn && board.length > 0 && (
            <span className="new-set-hint">drop in open space = new set</span>
          )}
        </DropZone>

        <DropZone id="rack" className="rack" aria-label="Your rack">
          {rack.map((t) => (
            <DraggableTile key={t.id} tile={t} disabled={!myTurn} highlighted={hintSet.has(t.id)} />
          ))}
        </DropZone>
      </DndContext>

      <Controls />
      {game.status === 'Finished' && <WinnerOverlay />}
    </div>
  );
}

function TopBar() {
  const game = useStore((s) => s.game)!;
  return (
    <div className="top-bar">
      <div className="players">
        {game.players.map((p) => (
          <div
            key={p.id}
            className={`player-chip ${p.id === game.currentPlayerId ? 'active' : ''} ${
              p.isConnected ? '' : 'offline'
            }`}
          >
            <span className="player-name">{p.name}</span>
            {p.isAi && <span className="tag">AI</span>}
            <span className="rack-count">{p.rackCount}🁢</span>
          </div>
        ))}
      </div>
      <div className="top-right">
        <span className="draw-pile">Pool: {game.drawPileCount}</span>
        <TurnTimer />
      </div>
    </div>
  );
}

function Controls() {
  const game = useStore((s) => s.game)!;
  const draw = useStore((s) => s.draw);
  const commit = useStore((s) => s.commit);
  const undo = useStore((s) => s.undo);
  const undoAll = useStore((s) => s.undoAll);
  const organize = useStore((s) => s.organize);
  const requestHint = useStore((s) => s.requestHint);
  const toggleHint = useStore((s) => s.toggleHint);
  const hintEnabled = useStore((s) => s.hintEnabled);
  const working = useStore((s) => s.working);
  const undoStack = useStore((s) => s.undoStack);

  const myTurn = isMyTurn(game);
  const placedTiles = working ? game.yourRack.length - working.rack.length : 0;
  const canCommit = myTurn && placedTiles > 0;

  return (
    <div className="controls">
      <button className="btn primary" disabled={!canCommit} onClick={() => void commit()}>
        Play {placedTiles > 0 ? `(${placedTiles})` : ''}
      </button>
      <button className="btn" disabled={!myTurn} onClick={() => void draw()}>
        Draw &amp; pass
      </button>
      <button className="btn" disabled={undoStack.length === 0} onClick={undo}>
        Undo
      </button>
      <button className="btn" disabled={undoStack.length === 0} onClick={undoAll}>
        Undo all
      </button>
      <button className="btn" onClick={organize}>
        Auto-organize
      </button>
      {hintEnabled && (
        <button className="btn" disabled={!myTurn} onClick={() => void requestHint()}>
          💡 Hint
        </button>
      )}
      <label className="hint-toggle">
        <input type="checkbox" checked={hintEnabled} onChange={toggleHint} />
        Hints
      </label>
    </div>
  );
}

function WinnerOverlay() {
  const game = useStore((s) => s.game)!;
  const leave = useStore((s) => s.leave);
  const winner = game.players.find((p) => p.id === game.winnerId);
  const youWon = game.winnerId === game.yourPlayerId;
  return (
    <div className="overlay">
      <div className="card winner-card">
        <h2>{youWon ? '🎉 You win!' : `${winner?.name ?? 'Someone'} wins!`}</h2>
        <button className="btn primary" onClick={leave}>
          Back to lobby
        </button>
      </div>
    </div>
  );
}

// ---- dnd-kit wrappers ----

function DraggableTile({ tile, disabled, highlighted }: { tile: Tile; disabled: boolean; highlighted: boolean }) {
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: String(tile.id),
    disabled,
  });
  return (
    <div
      ref={setNodeRef}
      {...listeners}
      {...attributes}
      className={`tile-wrap ${isDragging ? 'is-dragging' : ''} ${disabled ? 'locked' : ''}`}
    >
      <TileView tile={tile} highlighted={highlighted} />
    </div>
  );
}

function DropZone({
  id,
  className,
  children,
  ...rest
}: {
  id: string;
  className: string;
  children: React.ReactNode;
  'aria-label'?: string;
}) {
  const { setNodeRef, isOver } = useDroppable({ id });
  return (
    <div ref={setNodeRef} className={`${className} ${isOver ? 'over' : ''}`} {...rest}>
      {children}
    </div>
  );
}
