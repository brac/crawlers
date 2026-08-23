# Crawlers

**▶ Play it live at [crawlers.brac.dev](https://crawlers.brac.dev)**

A co-op dungeon crawler with a server-authoritative architecture. Play solo, or share a six-character room code with up to three other players. C# / ASP.NET Core backend over SignalR, React + Pixi.js client, Postgres for persistence.

The game and its lore are original. See [`CLAUDE.md`](./CLAUDE.md) for the design and architectural decisions.

## What's in here

| | |
|---|---|
| Backend | C# / ASP.NET Core 9, SignalR hubs at `/lobby` and `/game` |
| Frontend | React 19 + TypeScript, Pixi.js v8, Vite |
| Realtime | SignalR (HTTP/WebSocket negotiation) |
| Multiplayer | Room-code lobby, up to 4 players, shared fog of war, per-player descent, teammate revive, spectator mode |
| Procedural generation | Server-side BSP partitioning, deterministic per seed |
| Combat | Auto-battler, D&D-adjacent rolls (initiative, d20 vs AC, crits, AoO on flee). Structured per-event payload drives client animations. |
| Persistence | EF Core + Npgsql, migrations applied at startup |
| Container | Multi-stage Dockerfile, compose with Postgres |
| Art | [0x72 Dungeon Tileset II](https://0x72.itch.io/dungeontileset-ii) (16 px tiles, rendered 2×/3×), JSON-driven sprite manifest |

## Quick start (recommended: docker compose)

Requires Docker Desktop / Colima.

```sh
cp .env.example .env   # required: supplies POSTGRES_PASSWORD, which has no default
docker compose up --build
cd client && npm install && npm run dev
```

Open `http://localhost:5173`.

The compose stack runs `crawlers-server` (host `127.0.0.1:5238` → `8080` in the container) and `crawlers-postgres` on a private network; both host ports bind to loopback by default. Vite (`5173`) serves the client and proxies `/game`, `/lobby`, and `/api` to the server, so it is the only port exposed to the LAN.

## Local dev (no Docker)

You'll need .NET 9 SDK, Node 20+, and either:
- a Postgres instance with `ConnectionStrings__DefaultConnection` set, or
- nothing — with an empty connection string every persistence service falls back to an in-memory `Null*` implementation, so the game is fully playable, but run history, corpses, player identity, world stats, and the canonical dungeon are rebuilt from scratch on each restart.

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

On touch devices a D-pad + Flee / Descend buttons appear automatically (CSS-gated by `@media (pointer: coarse)`), and consumable inventory rows are tappable.

## LAN play

Vite binds on `0.0.0.0` (`server.host: true` in `vite.config.ts`). On the same Wi-Fi, hit `http://<your-machine-ip>:5173` from another device. Your machine's firewall may need to allow incoming connections to `node` for the Vite port.

## Tests

```sh
dotnet test server/Crawlers.slnx
```

Over 400 test cases (more than 280 test methods, expanded by xUnit theory rows) covering domain shapes, BSP generation, FOV, movement, engagement, combat (deterministic via a `ScriptedDice` test double), items, descent, entity placement, lobbies, persistence, and the multiplayer session and snapshot contracts.

## Project layout

```
crawlers/
├── CLAUDE.md                          ← project bible: design, architecture, build order
├── AI_BEHAVIOR.md                     ← enemy hunting/chase design and locked decisions
├── docker-compose.yml
├── .env.example
├── server/
│   ├── Dockerfile                     ← multi-stage, non-root, healthcheck
│   ├── Crawlers.slnx
│   ├── src/
│   │   ├── Crawlers.Domain/           ← shapes only, no logic
│   │   ├── Crawlers.Generation/       ← BSP + entity placement
│   │   └── Crawlers.Server/           ← ASP.NET Core, SignalR hubs, gameplay logic
│   └── tests/Crawlers.Tests/          ← xUnit
└── client/
    ├── vite.config.ts                 ← LAN bind + /game, /lobby, /api proxy
    ├── public/assets/dungeon/         ← 0x72 atlas + assets.json manifest
    └── src/
        ├── api/                       ← TS contracts mirroring server DTOs
        ├── game/                      ← Pixi renderer + asset loader
        ├── ui/                        ← HUD, combat log, inventory, mobile controls
        ├── App.tsx                    ← asset preload → identity → lobby phases
        └── Game.tsx                   ← /game connect, key handling, snapshot → render
```

## Architecture rules

- **Server owns truth.** The client sends intent and renders the state the server broadcasts. It computes no authoritative state: no movement or collision checks, no dice, no damage, no line of sight, no optimistic prediction. A few actions are additionally gated client-side for responsiveness (item use, revive adjacency, the descend and flee buttons, lobby host/join), and the server independently re-validates every one of them. Fog of war is computed server-side, but it is only partly enforced there: living entities are dropped from a snapshot unless their tile is Visible, while the full tile grid and room list go to every client every tick, paired with a per-player visibility array. The client skips drawing Hidden tiles and dims Explored ones to 0.35 alpha, so as far as map layout goes the fog is presentational, not authoritative. A modified client could read the whole floor. Do not treat the tile grid as a secret, and do not assume the client is trusted.
- **Sessions are rooms.** Even a solo run is a server-side room. `SessionBroadcaster` builds a snapshot per player, from that player's own floor and combat, and sends it to that one connection rather than group-broadcasting a shared payload. Multiplayer slotted into that shape instead of forcing a rewrite.
- **Domain shapes have no logic.** Generation depends on Domain; gameplay logic (movement, FOV, combat, items, descent) lives in `Crawlers.Server/Logic/`. Persistence is isolated under `Crawlers.Server/Persistence/`.

## Status

The single-player core, the visual-polish and combat-juice pass, co-op multiplayer, the persistent world, the content-and-depth pass, and enemy hunting AI have all shipped, and the game is deployed at [crawlers.brac.dev](https://crawlers.brac.dev). Rendering covers tile + character sprites from the 0x72 atlas, idle-loop "breathing", run-cycle during 250 ms ease-out tweens between tiles, direction facing via sprite flip, and per-event combat animations (lunge + red flash on hits, camera shake on crits, sidestep on misses, green pulse on heals).

Deliberately deferred: the Floor 4 capstone boss is a placeholder, and in-combat action choices beyond attack / use item / flee are designed but not built. [`CLAUDE.md`](./CLAUDE.md) carries the phase-by-phase status.
