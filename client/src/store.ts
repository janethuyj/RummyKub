import { create } from 'zustand';
import type { HubConnection } from '@microsoft/signalr';
import type { GameState, Tile } from './types';
import { ensureStarted, getConnection, hub } from './signalr';
import { findSets, organizeRack } from './rummikub';
import { cloneGrid, findTileCell, Grid, gridToSets, placeSetByDefault, reconcileGrid } from './board';

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
  autoPlay: () => Promise<void>;
  undo: () => void;
  undoAll: () => void;
  toggleHint: () => void;
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

  connect: async () => {
    conn = getConnection((state: GameState) => {
      // Every broadcast is server truth: reset the per-turn working copy and
      // clear the undo history (undo is scoped to the current turn). If the
      // player enabled auto-organize, keep the rack organized across draws.
      const rack = get().autoOrganize ? organizeRack(state.yourRack) : [...state.yourRack];
      // Preserve tiles the player already positioned; only auto-place new sets.
      const prevGrid = get().working?.grid ?? null;
      set({
        game: state,
        working: { grid: reconcileGrid(prevGrid, state.board), rack },
        undoStack: [],
        hintTileIds: [],
      });
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
    const { working, undoStack } = get();
    if (!working) return;

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
    });
  },

  organize: () => {
    const { working } = get();
    if (!working) return;
    // Auto-organize only reorders the rack (cosmetic), so it is not an undoable
    // move — leave the undo stack untouched. Turn on sticky auto-organize so the
    // rack stays organized after future draws.
    set({
      autoOrganize: true,
      working: { grid: working.grid, rack: organizeRack(working.rack) },
    });
  },

  autoPlay: async () => {
    const { working } = get();
    if (!working) return;
    // Find every complete set currently in hand, lay them on the board next to
    // whatever is already there, and try to play them. The server enforces the
    // 30-point first-meld rule, so if it's short the sets stay staged to adjust.
    const { sets } = findSets(working.rack);
    if (sets.length === 0) {
      set({ error: 'No complete sets in your hand to play.' });
      return;
    }
    // Add the new sets by the default convention, leaving existing tiles in place.
    const grid = cloneGrid(working.grid);
    for (const s of sets) placeSetByDefault(grid, s);
    const usedIds = new Set(sets.flat().map((t) => t.id));
    const rack = working.rack.filter((t) => !usedIds.has(t.id));
    set({ undoStack: [...get().undoStack, clone(working)], working: { grid, rack }, hintTileIds: [] });
    await get().commit();
  },

  undo: () => {
    const { undoStack } = get();
    if (undoStack.length === 0) return;
    const prev = undoStack[undoStack.length - 1];
    set({ working: clone(prev), undoStack: undoStack.slice(0, -1) });
  },

  undoAll: () => {
    // Restore the exact start-of-turn state (the first snapshot in the stack),
    // which keeps whatever board layout existed when the turn began.
    const { undoStack } = get();
    if (undoStack.length === 0) return;
    set({ working: clone(undoStack[0]), undoStack: [] });
  },

  toggleHint: () => set((s) => ({ hintEnabled: !s.hintEnabled, hintTileIds: [] })),
  clearError: () => set({ error: null }),
  leave: () => {
    localStorage.removeItem(STORAGE_KEY);
    set({ game: null, working: null, undoStack: [], hintTileIds: [], autoOrganize: false });
  },
}));

/** True when it is the local player's turn to act. */
export function isMyTurn(game: GameState | null): boolean {
  return !!game && game.status === 'Playing' && game.currentPlayerId === game.yourPlayerId;
}
