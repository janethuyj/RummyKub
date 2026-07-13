import * as signalR from '@microsoft/signalr';
import type { ActionResult, GameState, HintResult, JoinResult } from './types';

// One shared connection to the game hub. The hub is always same-origin:
// in dev, Vite proxies "/hub/game" to the ASP.NET Core server; in production
// the client is served by that same server. So a relative URL needs no CORS
// and no cookies/login (room-code play).
let connection: signalR.HubConnection | null = null;

export function getConnection(onState: (s: GameState) => void): signalR.HubConnection {
  if (connection) return connection;

  connection = new signalR.HubConnectionBuilder()
    .withUrl('/hub/game')
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  connection.on('GameState', (state: GameState) => onState(state));
  return connection;
}

export async function ensureStarted(conn: signalR.HubConnection): Promise<void> {
  if (conn.state === signalR.HubConnectionState.Disconnected) {
    await conn.start();
  }
}

// ---- Hub method wrappers ----

export const hub = {
  createRoom: (c: signalR.HubConnection, name: string) =>
    c.invoke<JoinResult>('CreateRoom', name),
  joinRoom: (c: signalR.HubConnection, code: string, name: string) =>
    c.invoke<JoinResult>('JoinRoom', code, name),
  rejoin: (c: signalR.HubConnection, code: string, playerId: string) =>
    c.invoke<JoinResult>('Rejoin', code, playerId),
  addAi: (c: signalR.HubConnection, code: string) =>
    c.invoke<ActionResult>('AddAiPlayer', code),
  startGame: (c: signalR.HubConnection, code: string) =>
    c.invoke<ActionResult>('StartGame', code),
  drawTile: (c: signalR.HubConnection, code: string) =>
    c.invoke<ActionResult>('DrawTile', code),
  commitMove: (c: signalR.HubConnection, code: string, board: number[][]) =>
    c.invoke<ActionResult>('CommitMove', code, board),
  requestHint: (c: signalR.HubConnection, code: string) =>
    c.invoke<HintResult>('RequestHint', code),
  setTimer: (c: signalR.HubConnection, code: string, seconds: number) =>
    c.invoke<ActionResult>('SetTimer', code, seconds),
};
