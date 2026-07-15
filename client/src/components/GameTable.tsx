import { Fragment } from 'react';
import {
  closestCenter,
  CollisionDetection,
  DndContext,
  DragEndEvent,
  PointerSensor,
  pointerWithin,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
} from '@dnd-kit/core';
import { CSS } from '@dnd-kit/utilities';
import type { Tile } from '../types';
import { BOARD_COLS, Cell, gridToSets } from '../board';
import { isMyTurn, useStore } from '../store';
import { TileView } from './TileView';
import { TurnTimer } from './TurnTimer';

// Prefer the cell directly under the pointer; fall back to the nearest droppable
// so a slightly-off drop still lands somewhere sensible.
const collision: CollisionDetection = (args) => {
  const within = pointerWithin(args);
  return within.length > 0 ? within : closestCenter(args);
};

export function GameTable() {
  const game = useStore((s) => s.game)!;
  const working = useStore((s) => s.working);
  const moveTile = useStore((s) => s.moveTile);
  const hintTileIds = useStore((s) => s.hintTileIds);
  const rackGaps = useStore((s) => s.rackGaps);
  const selectedIds = useStore((s) => s.selectedIds);
  const justDrawnIds = useStore((s) => s.justDrawnIds);
  const toggleSelect = useStore((s) => s.toggleSelect);

  const myTurn = isMyTurn(game);
  // One pointer sensor handles both mouse and touch. Dragging starts after an
  // 8px move; `touch-action: none` on tiles lets a touch-drag work while the
  // board still scrolls when you swipe on empty cells.
  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 8 } }));

  function handleDragEnd(e: DragEndEvent) {
    if (!e.over) return;
    const tileId = Number(e.active.id);
    const target = String(e.over.id);
    if (target === 'rack') {
      moveTile(tileId, 'rack');
    } else if (target.startsWith('cell-')) {
      const [, r, c] = target.split('-');
      moveTile(tileId, { r: Number(r), c: Number(c) });
    }
  }

  const grid = working?.grid ?? [];
  const rack = working?.rack ?? game.yourRack;
  const hintSet = new Set(hintTileIds);
  const gapSet = new Set(rackGaps);
  const selectedSet = new Set(selectedIds);
  const drawnSet = new Set(justDrawnIds);

  return (
    <div className="game-table">
      <TopBar />

      <DndContext sensors={sensors} collisionDetection={collision} onDragEnd={handleDragEnd}>
        <div className="play-area">
          <div className="board-scroll" aria-label="Board">
            <div className="grid" style={{ gridTemplateColumns: `repeat(${BOARD_COLS}, var(--cell-w))` }}>
              {grid.map((row, r) =>
                row.map((cell, c) => (
                  <BoardCell
                    key={`${r}-${c}`}
                    r={r}
                    c={c}
                    tile={cell}
                    myTurn={myTurn}
                    highlighted={!!cell && hintSet.has(cell.id)}
                  />
                )),
              )}
            </div>
          </div>

          <DropZone id="rack" className="rack" aria-label="Your rack">
            {rack.map((t, i) => (
              <Fragment key={t.id}>
                {gapSet.has(i) && <span className="rack-gap" aria-hidden="true" />}
                <DraggableTile
                  tile={t}
                  disabled={!myTurn}
                  highlighted={hintSet.has(t.id)}
                  selected={selectedSet.has(t.id)}
                  justDrawn={drawnSet.has(t.id)}
                  onSelect={myTurn ? () => toggleSelect(t.id) : undefined}
                />
              </Fragment>
            ))}
          </DropZone>
        </div>
      </DndContext>

      {myTurn && (
        <p className="board-caption">
          Drag tiles into the grid, or tap tiles to select and use Play selected.
        </p>
      )}

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
  const autoPlay = useStore((s) => s.autoPlay);
  const undo = useStore((s) => s.undo);
  const undoAll = useStore((s) => s.undoAll);
  const organize = useStore((s) => s.organize);
  const requestHint = useStore((s) => s.requestHint);
  const toggleHint = useStore((s) => s.toggleHint);
  const toggleSound = useStore((s) => s.toggleSound);
  const hintEnabled = useStore((s) => s.hintEnabled);
  const soundEnabled = useStore((s) => s.soundEnabled);
  const autoOrganize = useStore((s) => s.autoOrganize);
  const working = useStore((s) => s.working);
  const undoStack = useStore((s) => s.undoStack);
  const selectedIds = useStore((s) => s.selectedIds);
  const leave = useStore((s) => s.leave);

  const myTurn = isMyTurn(game);
  // Count tiles actually added to the board (on the grid but not in the committed
  // board) — the same thing the server checks — so Play never enables when the
  // server would reject with "you must place at least one tile".
  const committedIds = new Set(game.board.flat().map((t) => t.id));
  const placedTiles = working
    ? gridToSets(working.grid).flat().filter((t) => !committedIds.has(t.id)).length
    : 0;
  const canCommit = myTurn && placedTiles > 0;
  const hasSelection = selectedIds.length > 0;

  return (
    <div className="controls">
      <button className="btn primary" disabled={!canCommit} onClick={() => void commit()}>
        Play {placedTiles > 0 ? `(${placedTiles})` : ''}
      </button>
      <button
        className="btn"
        disabled={!myTurn}
        title={
          hasSelection
            ? 'Lay the selected tiles onto the board (then press Play to end your turn)'
            : 'Lay every complete set from your hand onto the board (then press Play to end your turn)'
        }
        onClick={autoPlay}
      >
        {hasSelection ? `▶ Play selected (${selectedIds.length})` : '⚡ Auto-play'}
      </button>
      <button className="btn" disabled={!myTurn} onClick={() => void draw()}>
        Draw &amp; pass
      </button>
      <div className="undo-row">
        <button className="btn" disabled={undoStack.length === 0} onClick={undo}>
          Undo
        </button>
        <button className="btn" disabled={undoStack.length === 0} onClick={undoAll}>
          Undo all
        </button>
      </div>
      <button
        className={`btn ${autoOrganize ? 'active' : ''}`}
        title={autoOrganize ? 'Keeping your rack organized after each draw' : 'Group valid sets and sort your rack'}
        onClick={organize}
      >
        Auto-organize{autoOrganize ? ' ✓' : ''}
      </button>
      {hintEnabled && (
        <button className="btn" disabled={!myTurn} onClick={() => void requestHint()}>
          💡 Hint
        </button>
      )}
      <button
        className="btn tiny"
        onClick={toggleSound}
        title={soundEnabled ? 'Sound on' : 'Sound off'}
      >
        {soundEnabled ? '🔊' : '🔇'}
      </button>
      <label className="hint-toggle">
        <input type="checkbox" checked={hintEnabled} onChange={toggleHint} />
        Hints
      </label>
      <button
        className="btn exit-btn"
        title="Leave the game"
        onClick={() => {
          if (window.confirm('Leave this game and return to the lobby?')) leave();
        }}
      >
        Exit
      </button>
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

function BoardCell({
  r,
  c,
  tile,
  myTurn,
  highlighted,
}: {
  r: number;
  c: number;
  tile: Cell;
  myTurn: boolean;
  highlighted: boolean;
}) {
  const { setNodeRef, isOver } = useDroppable({ id: `cell-${r}-${c}` });
  return (
    <div ref={setNodeRef} className={`cell ${isOver ? 'over' : ''} ${tile ? 'filled' : ''}`}>
      {tile && <DraggableTile tile={tile} disabled={!myTurn} highlighted={highlighted} />}
    </div>
  );
}

function DraggableTile({
  tile,
  disabled,
  highlighted,
  selected,
  justDrawn,
  onSelect,
}: {
  tile: Tile;
  disabled: boolean;
  highlighted: boolean;
  selected?: boolean;
  justDrawn?: boolean;
  onSelect?: () => void;
}) {
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: String(tile.id),
    disabled,
  });
  // Follow the pointer while dragging (and float above other tiles) so it is clear
  // where the tile will land.
  const style = transform
    ? { transform: CSS.Translate.toString(transform), zIndex: 20 }
    : undefined;
  return (
    <div
      ref={setNodeRef}
      style={style}
      {...listeners}
      {...attributes}
      onClick={onSelect}
      className={
        `tile-wrap ${isDragging ? 'is-dragging' : ''} ${disabled ? 'locked' : ''} ` +
        `${selected ? 'selected' : ''} ${justDrawn ? 'just-drawn' : ''}`
      }
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
