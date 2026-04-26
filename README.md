# Crawlers

A single-player dungeon crawler with a multiplayer-ready, server-authoritative architecture. C# / ASP.NET Core backend over SignalR, React + Pixi.js client, Postgres for persistence.

The game and its lore are original. See [`CLAUDE.md`](./CLAUDE.md) for the design and architectural decisions.

## What's in here

| | |
|---|---|
| Backend | C# / ASP.NET Core 9, SignalR hub at `/game` |
| Frontend | React 19 + TypeScript, Pixi.js v8, Vite |
| Realtime | SignalR (HTTP/WebSocket negotiation) |
| Procedural generation | Server-side BSP partitioning, deterministic per seed |
| Combat | Auto-battler, D&D-adjacent rolls (initiative, d20 vs AC, crits, AoO on flee) |
| Persistence | EF Core + Npgsql, migrations applied at startup |
| Container | Multi-stage Dockerfile, compose with Postgres |

## Quick start (recommended: docker compose)

Requires Docker Desktop / Colima.

```sh
docker compose up --build
cd client && npm install && npm run dev
```

Open `http://localhost:5173`.

The compose stack runs `crawlers-server` (port 5238 → 8080 in container) and `crawlers-postgres` on a private network. Vite (`5173`) serves the client and proxies `/game` to the server, so a single port is exposed to the LAN.

## Local dev (no Docker)

You'll need .NET 9 SDK, Node 20+, and either:
- a Postgres instance with `ConnectionStrings__DefaultConnection` set, or
- nothing — the server runs without a DB and skips run-history persistence.

```sh
# server (terminal 1)
dotnet run --project server/src/Crawlers.Server --launch-profile http

# client (terminal 2)
cd client && npm install && npm run dev
```

## Controls

| Key | Action |
|---|---|
| `W` `A` `S` `D` / arrows | Move (Exploration only) |
| `F` | Flee (Combat only) — adjacent enemy gets one attack of opportunity |
| `1`–`9` | Use the Nth consumable in your inventory (Combat: replaces attack for the round; Exploration: immediate) |
| `>` or `.` | Descend stairs (must be standing on stairs-down) |

## LAN play

Vite binds on `0.0.0.0` (`server.host: true` in `vite.config.ts`). On the same Wi-Fi, hit `http://<your-mac-ip>:5173` from another device. Your machine's firewall may need to allow incoming connections to `node` for the Vite port.

## Tests

```sh
dotnet test server/Crawlers.slnx
```

161 tests covering domain shapes, BSP generation, FOV, movement, engagement, combat (deterministic via a `ScriptedDice` test double), items, descent, and entity placement.

## Project layout

```
crawlers/
├── CLAUDE.md                          ← project bible: design, architecture, build order
├── docker-compose.yml
├── .env.example
├── server/
│   ├── Dockerfile                     ← multi-stage, non-root, healthcheck
│   ├── Crawlers.slnx
│   ├── src/
│   │   ├── Crawlers.Domain/           ← shapes only, no logic
│   │   ├── Crawlers.Generation/       ← BSP + entity placement
│   │   └── Crawlers.Server/           ← ASP.NET Core, SignalR hub, gameplay logic
│   └── tests/Crawlers.Tests/          ← xUnit
└── client/
    ├── vite.config.ts                 ← LAN bind + /game proxy
    └── src/
        ├── api/                       ← TS contracts mirroring server DTOs
        ├── game/                      ← Pixi renderer
        ├── ui/                        ← HUD, combat log, inventory
        └── App.tsx                    ← connect → join → keydown
```

## Architecture rules

- **Server owns truth.** The client sends intent and renders the state the server broadcasts. No game logic on the client. Even fog-of-war filtering happens server-side — the client never sees tiles outside the player's awareness.
- **Sessions are rooms.** Even single-player sessions are server-side rooms with group broadcasts, so multiplayer is a slot-in extension rather than a rewrite.
- **Domain shapes have no logic.** Generation depends on Domain; gameplay logic (movement, FOV, combat, items, descent) lives in `Crawlers.Server/Logic/`. Persistence is isolated under `Crawlers.Server/Persistence/`.

## Status

All ten build-order steps are complete. See [`CLAUDE.md`](./CLAUDE.md) for the per-step rundown of what was built.
