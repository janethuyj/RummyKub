import { create } from 'zustand';
import type { HubConnection } from '@microsoft/signalr';
import type { GameState, Tile } from './types';
import { ensureStarted, getConnection, hub } from './signalr';
import { organizeRack } from './rummikub';

const STORAGE_KEY = 'rummykub.session';

interface Working {
  board: Tile[][];
  rack: Tile[];
}

interface SavedSession {
  roomCode: string;
  playerId: string;
}

/** Drop target: the rack, an existing set by index, or a brand-new set. */
export type Container = 'rack' | 'new' | { setIndex: number };

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
  board: w.board.map((s) => [...s]),
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
      set({
        game: state,
        working: { board: state.board.map((s) => [...s]), rack },
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
    const board = working.board.filter((s) => s.length > 0).map((s) => s.map((t) => t.id));
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

    // Locate and detach the tile from wherever it currently is.
    let moved: Tile | undefined;
    const board = working.board.map((s) => s.filter((t) => (t.id === tileId ? ((moved = t), false) : true)));
    let rack = working.rack.filter((t) => (t.id === tileId ? ((moved = t), false) : true));
    if (!moved) return;

    if (to === 'rack') {
      rack = [...rack, moved];
    } else if (to === 'new') {
      board.push([moved]);
    } else {
      const idx = to.setIndex;
      if (idx >= 0 && idx < board.length) board[idx] = [...board[idx], moved];
      else board.push([moved]);
    }

    const cleaned = board.filter((s) => s.length > 0);
    set({
      undoStack: [...undoStack, clone(working)],
      working: { board: cleaned, rack },
      hintTileIds: [],
    });
  },

  organize: () => {
    const { working, undoStack } = get();
    if (!working) return;
    // Turn on sticky auto-organize so the rack stays organized after future draws.
    set({
      autoOrganize: true,
      undoStack: [...undoStack, clone(working)],
      working: { board: working.board, rack: organizeRack(working.rack) },
    });
  },

  undo: () => {
    const { undoStack } = get();
    if (undoStack.length === 0) return;
    const prev = undoStack[undoStack.length - 1];
    set({ working: clone(prev), undoStack: undoStack.slice(0, -1) });
  },

  undoAll: () => {
    const { game } = get();
    if (!game) return;
    set({
      working: { board: game.board.map((s) => [...s]), rack: [...game.yourRack] },
      undoStack: [],
    });
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
