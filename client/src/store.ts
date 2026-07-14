import { create } from 'zustand';
import type { HubConnection } from '@microsoft/signalr';
import type { GameState, Tile } from './types';
import { ensureStarted, getConnection, hub } from './signalr';
import { findSets, organizeRack } from './rummikub';
import { cloneGrid, findTileCell, Grid, gridToSets, placeSetByDefault, reconcileGrid } from './board';
import { playTileDrawn, playTilePlayed, unlockAudio } from './sound';

const boardTileCount = (board: Tile[][]) => board.reduce((n, s) => n + s.length, 0);

const STORAGE_KEY = 'rummykub.session';

interface Working {
  grid: Grid;
  rack: Tile[];
}

interface SavedSession {
  roomCode: string;
  playerId: string;
}

/** Drop target: back to the rack, or a specific board cell. */
export type Container = 'rack' | { r: number; c: number };

interface Store {
  connected: boolean;
  game: GameState | null;
  working: Working | null;
  undoStack: Working[];
  error: string | null;
  hintEnabled: boolean;
  hintTileIds: number[];
  autoOrganize: boolean;
  rackGaps: number[];
  selectedIds: number[];
  justDrawnIds: number[];
  soundEnabled: boolean;

  connect: () => Promise<void>;
  createRoom: (name: string) => Promise<void>;
  joinRoom: (code: string, name: string) => Promise<void>;
  addAi: () => Promise<void>;
  startGame: () => Promise<void>;
  draw: () => Promise<void>;
  commit: () => Promise<void>;
  requestHint: () => Promise<void>;
  setTimer: (seconds: number) => Promise<void>;

  moveTile: (tileId: number, to: Container) => void;
  organize: () => void;
  autoPlay: () => void;
  toggleSelect: (tileId: number) => void;
  undo: () => void;
  undoAll: () => void;
  toggleHint: () => void;
  toggleSound: () => void;
  clearError: () => void;
  leave: () => void;
}

let conn: HubConnection | null = null;

function loadSession(): SavedSession | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as SavedSession) : null;
  } catch {
    return null;
  }
}

function saveSession(s: SavedSession) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(s));
}

const clone = (w: Working): Working => ({
  grid: cloneGrid(w.grid),
  rack: [...w.rack],
});

export const useStore = create<Store>((set, get) => ({
  connected: false,
  game: null,
  working: null,
  undoStack: [],
  error: null,
  hintEnabled: true,
  hintTileIds: [],
  autoOrganize: false,
  rackGaps: [],
  selectedIds: [],
  justDrawnIds: [],
  soundEnabled: true,

  connect: async () => {
    conn = getConnection((state: GameState) => {
      // Every broadcast is server truth: reset the per-turn working copy and
      // clear the undo history (undo is scoped to the current turn).
      const prev = get();

      // Sound cues: someone drew (draw pile shrank) or played (board grew).
      if (prev.soundEnabled && prev.game) {
        if (state.drawPileCount < prev.game.drawPileCount) playTileDrawn();
        else if (boardTileCount(state.board) > boardTileCount(prev.game.board)) playTilePlayed();
      }

      // Briefly highlight tiles that just arrived in hand (a draw), but not the
      // initial deal or a reconnect.
      let justDrawnIds = prev.justDrawnIds;
      const prevRack = prev.game?.yourRack;
      if (prevRack && prevRack.length > 0) {
        const prevIds = new Set(prevRack.map((t) => t.id));
        const drawn = state.yourRack.filter((t) => !prevIds.has(t.id)).map((t) => t.id);
        if (drawn.length > 0 && drawn.length <= 2) {
          justDrawnIds = drawn;
          setTimeout(
            () => set((s) => ({ justDrawnIds: s.justDrawnIds.filter((id) => !drawn.includes(id)) })),
            2600,
          );
        }
      }

      // Keep the rack organized across draws when auto-organize is on.
      const org = prev.autoOrganize ? organizeRack(state.yourRack) : null;
      const rack = org ? org.ordered : [...state.yourRack];
      // Preserve tiles the player already positioned; only auto-place new sets.
      const prevGrid = prev.working?.grid ?? null;
      set({
        game: state,
        working: { grid: reconcileGrid(prevGrid, state.board), rack },
        undoStack: [],
        hintTileIds: [],
        rackGaps: org ? org.gaps : [],
        selectedIds: [],
        justDrawnIds,
      });
    });

    // After an automatic reconnect the server sees a NEW connection id, so we
    // must re-bind our seat or we stop receiving broadcasts (the "had to refresh"
    // bug). Rejoining rebinds the connection and pushes fresh state.
    conn.onreconnected(() => {
      const saved = loadSession();
      if (saved && conn) void hub.rejoin(conn, saved.roomCode, saved.playerId);
    });

    await ensureStarted(conn);
    set({ connected: true });

    // Attempt to reclaim a seat after a refresh/reconnect.
    const saved = loadSession();
    if (saved) {
      const res = await hub.rejoin(conn, saved.roomCode, saved.playerId);
      if (!res.ok) localStorage.removeItem(STORAGE_KEY);
    }
  },

  createRoom: async (name) => {
    if (!conn) return;
    const res = await hub.createRoom(conn, name);
    if (res.ok && res.roomCode && res.playerId) {
      saveSession({ roomCode: res.roomCode, playerId: res.playerId });
    } else {
      set({ error: res.error ?? 'Could not create room.' });
    }
  },

  joinRoom: async (code, name) => {
    if (!conn) return;
    const res = await hub.joinRoom(conn, code.toUpperCase(), name);
    if (res.ok && res.roomCode && res.playerId) {
      saveSession({ roomCode: res.roomCode, playerId: res.playerId });
    } else {
      set({ error: res.error ?? 'Could not join room.' });
    }
  },

  addAi: async () => {
    const { game } = get();
    if (!conn || !game) return;
    const res = await hub.addAi(conn, game.roomCode);
    if (!res.ok) set({ error: res.error });
  },

  startGame: async () => {
    const { game } = get();
    if (!conn || !game) return;
    const res = await hub.startGame(conn, game.roomCode);
    if (!res.ok) set({ error: res.error });
  },

  draw: async () => {
    const { game } = get();
    if (!conn || !game) return;
    const res = await hub.drawTile(conn, game.roomCode);
    if (!res.ok) set({ error: res.error });
  },

  commit: async () => {
    const { game, working } = get();
    if (!conn || !game || !working) return;
    const board = gridToSets(working.grid).map((s) => s.map((t) => t.id));
    const res = await hub.commitMove(conn, game.roomCode, board);
    if (!res.ok) set({ error: res.error }); // keep working state so the player can fix it
  },

  requestHint: async () => {
    const { game } = get();
    if (!conn || !game) return;
    const res = await hub.requestHint(conn, game.roomCode);
    if (res.shouldDraw) {
      set({ error: 'No move available — draw a tile.', hintTileIds: [] });
    } else {
      set({ hintTileIds: res.suggestedTileIds });
    }
  },

  setTimer: async (seconds) => {
    const { game } = get();
    if (!conn || !game) return;
    const res = await hub.setTimer(conn, game.roomCode, seconds);
    if (!res.ok) set({ error: res.error });
  },

  moveTile: (tileId, to) => {
    const { working, undoStack, game } = get();
    if (!working) return;

    // Committed board tiles stay on the board — you rearrange them there, you don't
    // take them back into your hand. This prevents accidentally "removing" a board
    // tile by dropping it on the rack. (Tiles you placed this turn can still return.)
    if (to === 'rack' && game && game.board.some((s) => s.some((t) => t.id === tileId))) return;

    // Dropping onto an occupied cell is a no-op (leave the tile where it is).
    if (to !== 'rack' && working.grid[to.r]?.[to.c]) return;

    // Find the tile in the rack or on the grid.
    const fromRack = working.rack.find((t) => t.id === tileId);
    const cell = fromRack ? null : findTileCell(working.grid, tileId);
    const moved = fromRack ?? (cell ? working.grid[cell.r][cell.c]! : undefined);
    if (!moved) return;

    const grid = cloneGrid(working.grid);
    let rack = working.rack.filter((t) => t.id !== tileId);
    if (cell) grid[cell.r][cell.c] = null;

    if (to === 'rack') rack = [...rack, moved];
    else grid[to.r][to.c] = moved;

    set({
      undoStack: [...undoStack, clone(working)],
      working: { grid, rack },
      hintTileIds: [],
      rackGaps: [], // manual rearranging drops the organized grouping
      selectedIds: [],
    });
  },

  organize: () => {
    const { working } = get();
    if (!working) return;
    // Auto-organize only reorders the rack (cosmetic), so it is not an undoable
    // move — leave the undo stack untouched. Turn on sticky auto-organize so the
    // rack stays organized after future draws.
    const org = organizeRack(working.rack);
    set({
      autoOrganize: true,
      working: { grid: working.grid, rack: org.ordered },
      rackGaps: org.gaps,
      selectedIds: [],
    });
  },

  // Lay complete sets onto the board WITHOUT ending the turn, so the player can
  // still undo/adjust before committing with Play. If tiles are selected, only
  // those are considered; otherwise the whole hand is.
  autoPlay: () => {
    const { working, selectedIds } = get();
    if (!working) return;
    const pool = selectedIds.length > 0
      ? working.rack.filter((t) => selectedIds.includes(t.id))
      : working.rack;

    const { sets } = findSets(pool);
    if (sets.length === 0) {
      set({ error: selectedIds.length > 0 ? 'The selected tiles do not form a complete set.' : 'No complete sets in your hand to play.' });
      return;
    }
    const grid = cloneGrid(working.grid);
    for (const s of sets) placeSetByDefault(grid, s);
    const usedIds = new Set(sets.flat().map((t) => t.id));
    const rack = working.rack.filter((t) => !usedIds.has(t.id));
    set({
      undoStack: [...get().undoStack, clone(working)],
      working: { grid, rack },
      hintTileIds: [],
      rackGaps: [],
      selectedIds: [],
    });
  },

  toggleSelect: (tileId) => {
    set((s) => ({
      selectedIds: s.selectedIds.includes(tileId)
        ? s.selectedIds.filter((id) => id !== tileId)
        : [...s.selectedIds, tileId],
    }));
  },

  undo: () => {
    const { undoStack } = get();
    if (undoStack.length === 0) return;
    const prev = undoStack[undoStack.length - 1];
    set({ working: clone(prev), undoStack: undoStack.slice(0, -1), selectedIds: [] });
  },

  undoAll: () => {
    // Restore the exact start-of-turn state (the first snapshot in the stack),
    // which keeps whatever board layout existed when the turn began.
    const { undoStack } = get();
    if (undoStack.length === 0) return;
    set({ working: clone(undoStack[0]), undoStack: [], selectedIds: [] });
  },

  toggleHint: () => set((s) => ({ hintEnabled: !s.hintEnabled, hintTileIds: [] })),
  toggleSound: () =>
    set((s) => {
      const soundEnabled = !s.soundEnabled;
      // Turning sound on inside this click gesture unlocks audio and plays a cue,
      // so the player immediately confirms it works.
      if (soundEnabled) {
        unlockAudio();
        playTilePlayed();
      }
      return { soundEnabled };
    }),
  clearError: () => set({ error: null }),
  leave: () => {
    localStorage.removeItem(STORAGE_KEY);
    set({
      game: null, working: null, undoStack: [], hintTileIds: [], autoOrganize: false,
      rackGaps: [], selectedIds: [], justDrawnIds: [],
    });
  },
}));

/** True when it is the local player's turn to act. */
export function isMyTurn(game: GameState | null): boolean {
  return !!game && game.status === 'Playing' && game.currentPlayerId === game.yourPlayerId;
}
