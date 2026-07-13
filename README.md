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
Requires the **.NET 9 SDK** and **Node 18+**.

```bash
# Backend
dotnet build
dotnet test                              # run engine unit tests
dotnet run --project server/Rummikub.Server

# Frontend (once scaffolded)
cd client && npm install && npm run dev
```

## Status
Under active development. See the build plan for the roadmap.
