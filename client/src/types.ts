// Mirrors the server DTOs in Rummikub.Server/Contracts/Dtos.cs

export type TileColor = 'red' | 'blue' | 'black' | 'orange';

export interface Tile {
  id: number;
  color: TileColor | null; // null for jokers
  number: number;
  isJoker: boolean;
}

export interface PlayerState {
  id: string;
  name: string;
  isAi: boolean;
  rackCount: number;
  hasMelded: boolean;
  isConnected: boolean;
  isHost: boolean;
}

export type GameStatus = 'Lobby' | 'Playing' | 'Finished';

export interface GameState {
  roomCode: string;
  status: GameStatus;
  players: PlayerState[];
  board: Tile[][];
  currentPlayerIndex: number;
  currentPlayerId: string | null;
  winnerId: string | null;
  drawPileCount: number;
  timerSeconds: number;
  turnDeadlineUnixMs: number | null;
  yourPlayerId: string;
  yourRack: Tile[];
}

export interface JoinResult {
  ok: boolean;
  error: string | null;
  roomCode: string | null;
  playerId: string | null;
}

export interface ActionResult {
  ok: boolean;
  error: string | null;
}

export interface HintResult {
  shouldDraw: boolean;
  suggestedTileIds: number[];
  suggestedBoard: number[][] | null;
}

export interface MovePreview {
  playerId: string;
  board: Tile[][];
}
