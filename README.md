# RummyKub

A browser-based [Rummikub](https://en.wikipedia.org/wiki/Rummikub) game: play online with friends via a room code, or offline against AI. Up to 4 players.

## Features
- Online multiplayer (private **room code**) or offline **vs AI**
- Up to **4 players** (humans and/or AI)
- **Auto-organize** your rack
- **AI hint** (toggle on/off)
- **Undo** one move / undo all (within your turn)
- Per-turn **timer**: 30s / 60s / off
- Single-winner rounds

## Tech Stack
| Layer | Tech |
|-------|------|
| Frontend | React + TypeScript + Vite, dnd-kit, Zustand |
| Realtime | SignalR |
| Backend | ASP.NET Core (C#) |
| Game engine + AI | Pure C# class library (`Rummikub.Engine`) |
| Persistence | In-memory (room-code play; no DB) |

## Project Layout
```
server/
  Rummikub.Engine/        Domain model, rules, AI (pure C#, unit tested)
  Rummikub.Engine.Tests/  xUnit tests for the engine
  Rummikub.Server/        ASP.NET Core + SignalR hub, room manager
client/                   React + TS front end (added in a later step)
```

## Development
Requires the **.NET 9 SDK** and **Node 20+**.

Run the backend and frontend in two terminals:

```bash
# Terminal 1 — backend (SignalR hub on http://localhost:5080)
dotnet run --project server/Rummikub.Server

# Terminal 2 — frontend (Vite dev server on http://localhost:5173)
cd client
npm install
npm run dev
```

Open http://localhost:5173. The Vite dev server proxies `/hub/game` to the
backend, so there is no CORS to configure locally. To test multiplayer, open a
second browser tab/window, create a room in one, and join by code in the other
(or click **Add AI player** to fill seats).

### Tests
```bash
dotnet test          # 52 engine unit tests (rules, meld, AI, undo, session)
```

### Production build
```bash
docker build -t rummykub .    # multi-stage: builds client + server into one image
docker run -p 8080:8080 rummykub
```
The server serves the built React app from `wwwroot`, so a single container
hosts both halves. See the build plan for the full deployment/scaling notes.

## Status
Playable end-to-end: online room-code multiplayer, offline vs AI, up to 4
players, auto-organize, hints, undo one/all, per-turn timer, single-winner rounds.
