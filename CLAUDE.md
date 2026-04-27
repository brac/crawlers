# CLAUDE.md — Project Bible

## What This Is

A single-player dungeon crawler with a multiplayer-ready architecture. Built to be a portfolio-quality project demonstrating real-time server-authoritative game architecture, procedural generation, and D&D-adjacent combat systems.

The game is **not** based on any existing IP. Tone, mechanics, and universe are original.

---

## Current Status

Build order progress (see "Project Build Order" below for full list):

- [x] **Step 1 — Data models.** `Crawlers.Domain` class library, all core shapes, builds clean.
- [x] **Step 2 — Floor generation (BSP).** `Crawlers.Generation` produces valid floors with full connectivity, deterministic seeds, and an ASCII debug renderer.
- [x] **Step 3 — Server + SignalR skeleton.** `Crawlers.Server` (ASP.NET Core, net9.0) hosts `/game` SignalR hub. `SessionManager` (in-memory) creates session + floor + player on join; client receives initial `GameStateSnapshotDto`.
- [x] **Step 4 — Renderer.** Vite + React + TypeScript + Pixi.js v8 client in `client/`. Single-page HUD overlay + Pixi canvas. Visually verified on Mac and Windows (LAN host via `vite.config.ts` proxy → `localhost:5238`).
- [x] **Step 5 — Player movement.** WASD + arrow keys → `Move(direction)` hub method → server validates → recomputes FOV (Bresenham + Euclidean radius) → updates fog → broadcasts to session group → client re-renders with per-tile alpha (Hidden/Explored/Visible). 126 tests passing.
- [x] **Step 6 — Entity placement + LOS.** Husks placed deterministically (4 per floor, never on stairs, never in spawn room). FOV-filtered enemy DTOs. Engagement triggers on Chebyshev≤3 + Visible-in-fog → `Session.Mode = Combat`; `Move` rejected while in Combat. 136 tests passing.
- [x] **Step 6.5 — Dockerize backend.** Multi-stage Dockerfile (SDK → aspnet, non-root `app` user, port 8080, `/health` HEALTHCHECK via curl). `docker-compose.yml` runs `postgres:16-alpine` + server on a private network with `depends_on` waiting on Postgres healthy. All env-driven: `Cors__AllowedOrigins`, `ConnectionStrings__DefaultConnection`, `ASPNETCORE_ENVIRONMENT`. Verified end-to-end: container builds clean, `/health` 200, `/game/negotiate` returns all transports, **WebSocket upgrade succeeds with `HTTP/1.1 101 Switching Protocols`** through the container.
- [x] **Step 7 — Combat.** Auto-battler with streamed round-by-round log. `CombatService` (initiative, d20 attack rolls vs AC, nat-20 crits, nat-1 fumbles, AoO on flee). `CombatRunner` background task ticks every 900ms per fighting session, locked via `SessionState.SyncRoot`, broadcasts via `IHubContext<GameHub, IGameClient>`. Hub adds `Flee`. Outcomes: PlayerWon (enemy → Dead, Mode → Exploration), PlayerFled (Mode → Exploration), PlayerDied (Mode → Resolution; refresh required, persistence is Step 9). Client adds HP bar + scrolling combat log + F-key.
- [x] **Step 8 — Items + inventory.** `Item` extended with `Effect`/`EffectValue`. `ItemTemplates` (Healing Draught, Bone Charm). `ItemEffects` exposes passive bonuses; `PlayerAttack` applies `+atk` from inventory. Loot drops on `PlayerWon` at corpse tile. `MovementService` picks up `Type=Item` entities. `Hub.UseItem(itemId)` works in both Combat (queued via `CombatRunner.RequestUseItem`, replaces the round's attack) and Exploration (immediate via `ItemUseHelper.Apply`, broadcast). Client renders teal squares for floor items, inventory panel with hotkeys 1-9.
- [x] **Step 9 — Persistence.** EF Core 9 + Npgsql provider. `CrawlersDbContext` + `RunHistoryEntry` + initial migration applied at startup via `db.Database.MigrateAsync()`. `IRunHistoryService` with `RunHistoryService` (Postgres) and `NullRunHistoryService` (no-op fallback when `ConnectionStrings:DefaultConnection` is empty). `CombatRunner` records death rows after the final broadcast: id/player/session/seed/cause-of-death/kill count/HP/inventory count/timestamps. Verified: row written, schema correct.
- [x] **Step 10 — Polish + balancing.** Restart button on the death banner (no refresh required; `JoinNewSession` cleans up the prior session in-memory). Loot drop rate balanced (40% nothing / 40% Heal / 20% Charm). Three enemy archetypes: Husk (baseline), Rasper (fast/fragile), Hulk (slow/tough), with distinct colors + sizes on the renderer. Multi-floor descent: stand on stairs-down + press `>` to generate the next floor with deterministic seed `InitialSeed + (floor-1)`, +1 enemy per floor (cap 10), inventory + HP carry over. 161 tests passing.

- [x] **Visual polish phase 1.** All eight steps in `VISUAL_POLISH.md` shipped: 0x72 Dungeon Tileset II atlas + JSON manifest at `client/public/assets/dungeon/`, Pixi `Assets` preload with loading state, sprite-based tile rendering at native 16 px scaled 2× (mobile) or 3× (desktop) with integer scale picked from viewport, `AnimatedSprite` characters with idle-loop "breathing" + run cycle during 250 ms ease-out tweens, sprite-flip facing on horizontal moves, camera follow on the tweened player position. Mobile D-pad + Flee/Descend buttons gated by `@media (pointer: coarse)`, tappable inventory rows.
- [x] **Combat juice (server-side payload + client anims).** `CombatEvent` carries structured `Kind`/`ActorId`/`TargetId`/`Damage`; renderer plays per-event animations during combat: lunge + red flash on Hit, heavier flash + camera shake on Crit, sidestep dodge on Miss, jitter on Fumble, green pulse on Heal. Killing-blow Hit animates before the dying enemy sprite is destroyed. 161 tests passing.

---

## Solution Layout

```
crawlers/
├── CLAUDE.md
└── server/
    ├── Crawlers.slnx
    └── src/
        ├── Crawlers.Domain/        ← Step 1: shapes only, no logic
        │   ├── Enums/
        │   └── Models/
        ├── Crawlers.Generation/    ← Step 2: BSP floor generator + ASCII renderer
        ├── Crawlers.Server/        ← Step 3+: ASP.NET Core + SignalR hub
        │   ├── Hubs/               (GameHub, IGameClient)
        │   ├── Sessions/           (SessionManager, SessionState)
        │   ├── Logic/              (FieldOfView, MovementService — gameplay logic)
        │   └── Contracts/          (DTOs + SnapshotMapper)
        └── ../tests/Crawlers.Tests/ ← xUnit; Domain + Generation + Server

crawlers/client/                    ← Step 4: React + TS + Pixi.js v8 + SignalR client
├── vite.config.ts                  (host: true; proxy /game → localhost:5238 with ws:true)
├── public/assets/dungeon/          (0x72 Dungeon Tileset II + assets.json manifest)
└── src/
    ├── api/                        (signalr.ts, types.ts — TS mirrors of server contracts)
    ├── game/                       (DungeonRenderer, DungeonView, assets.ts, tileColors)
    ├── ui/                         (Hud, CombatLog, Inventory, MobileControls)
    └── App.tsx                     (connect → join → keydown WASD → server)

crawlers/                           ← Step 6.5: container infrastructure
├── docker-compose.yml              (postgres:16-alpine + server, env-driven)
├── .env.example                    (override defaults; .env is gitignored)
├── .gitignore
└── server/
    ├── Dockerfile                  (multi-stage; aspnet:9.0 runtime, non-root, EXPOSE 8080)
    └── .dockerignore
```

Hub endpoint: `/game` (SignalR). Health check: `/health`. Server: `localhost:5238` (host) → `8080` (container). Client dev: `localhost:5173` (Vite, all interfaces).

### Configuration policy (Step 6.5)
- All environment-specific values flow through ASP.NET Core configuration. Override via env: `Cors__AllowedOrigins`, `ConnectionStrings__DefaultConnection`, `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`.
- `appsettings.json` carries dev-friendly defaults only — no secrets, no real connection strings.
- Compose composes the connection string from `POSTGRES_*` env vars; the server reaches Postgres via the compose-network alias `postgres:5432`.

Future projects: `Crawlers.Persistence` (Postgres, Step 9). Gameplay logic may be promoted out of `Crawlers.Server/Logic/` into a dedicated `Crawlers.Logic` project when combat (Step 7) lands.

### Layer Rules
- **Domain** holds data shapes only. No logic. No dependencies on other Crawlers projects.
- **Generation** depends on Domain and produces `Floor` instances. Pure functions of `(seed, config)`.
- All future server projects depend down the stack, never sideways or up.

### Debug Rendering Policy
- ASCII is the only server-side debug renderer (`FloorAsciiRenderer`). It belongs to dev/test tooling.
- No richer preview formats (SVG, Canvas, sprites) on the server side. Real visual rendering is the client's job and lives entirely in Step 4 with React + Pixi.js.
- The server never emits anything intended for human eyes other than logs and ASCII for tests/REPL.

### Visual Polish Phase 1 (assets + animations)
- Tileset: 0x72 Dungeon Tileset II v1.7 (single master atlas at `client/public/assets/dungeon/0x72_DungeonTilesetII_v1.7/`).
- All sprite coordinates are declared in `client/public/assets/dungeon/assets.json` — never hardcode atlas frame coords in TS. Schema and decisions live in `VISUAL_POLISH.md`.
- Native tile size is **16 px**. The renderer applies an integer scale (2× on phones, 3× on desktops) to `worldContainer`, picked from the smaller viewport dimension. Camera tracks the tweened player position, never the snapshot's tile coordinate, so movement glides.
- Animations: `AnimatedSprite` for characters (player + enemies). Idle plays a slow loop ("breathing"); run cycle plays during a 250 ms ease-out positional tween between tiles. Items remain `Sprite` (no anim).
- Combat events are structured: `CombatEvent { ActorId, TargetId, Kind, Damage, Description }`. The renderer ingests round events via a running watermark and queues per-event animations (Hit/Crit/Miss/Fumble/Heal). Killing-blow sprites are deferred from destruction until their pending anims drain.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | React + Pixi.js (dungeon rendering) + React (HUD/UI) |
| Realtime Transport | SignalR |
| Backend | C# / ASP.NET Core |
| Database | PostgreSQL |
| Dungeon Generation | C# server-side only |

### Why These Choices
- **Authoritative server**: All game logic lives on the server. The client renders state and sends intent. This is non-negotiable — it is the foundation that makes multiplayer a slot-in feature later.
- **SignalR**: Fits naturally in the C# ecosystem and handles both the single-player broadcast model and future multiplayer room model without rearchitecting.
- **Pixi.js**: 2D WebGL renderer. Handles top-down or isometric tile rendering efficiently. React wraps it for HUD, menus, and overlays.
- **Postgres**: Persists run history, character state, floor seeds.

---

## Architecture Principles

### The Golden Rule
**The server owns all truth. The client owns nothing.**

- Client sends *intent*: move north, use item, attempt flee
- Server validates intent, updates state, resolves any triggered logic
- Server broadcasts updated state to client(s)
- Client re-renders

This applies even in single player. There is no shortcut for single player that would need to be undone later.

### Multiplayer Readiness
Multiplayer is not being built now. However every architectural decision should make it a natural extension, not a rewrite.

Specifically:
- Game sessions are modeled as *rooms* on the server from day one, even if only one player occupies them
- Entity positions, floor state, and combat state are all server-held
- When a second player eventually joins a room, the server already owns all state — they just receive it on connect
- The "oh snap there's another player" moment is just rendering an entity the server is already tracking

### No Client-Side Game Logic
- Do not calculate movement validity on the client
- Do not calculate LOS on the client
- Do not resolve combat on the client
- The client may do *predictive rendering* for smoothness later, but never authoritative logic

---

## Game Modes (State Machine)

The game operates as a clear state machine. The server tracks which mode a session is in.

```
EXPLORATION → ENGAGEMENT → COMBAT → RESOLUTION → EXPLORATION
```

### Exploration Mode
- Player moves freely through the dungeon via directional input (WASD or equivalent)
- Server runs passive LOS checks each time player position updates
- No combat, no restrictions on movement
- Server checks proximity + LOS against all entities on the floor each move

### Engagement Triggered
- Conditions: an enemy enters LOS range AND is within a defined proximity threshold
- Both the player and the enemy entity "lock in" — the session transitions to COMBAT mode
- Movement is restricted during combat (player may still attempt flee or use items)

### Combat Mode
- Server auto-resolves combat in rounds
- Player does not control attack decisions — combat is auto-battled
- Server streams a combat log to the client describing what is happening each round
- Player may:
  - Use an item (if they have one)
  - Attempt to flee (triggers AoO check before the move executes)
- Combat is D&D-adjacent in feel: initiative, attack rolls, modifiers, crits

### Resolution
- Combat ends when one side flees or is dead
- If enemy flees: session returns to EXPLORATION, enemy entity removed or marked fled
- If player flees: session returns to EXPLORATION, player moves back, cooldown applies
- If enemy dies: loot/XP resolved server-side, session returns to EXPLORATION
- If player dies: permadeath — run ends, results written to DB

---

## Combat System

### Philosophy
Auto-battler. The server resolves the entire exchange. No input timing, no latency sensitivity, no client-side cheating surface.

### D&D-Adjacent Rules (not 1:1, inspired by)
- **Initiative**: Each entity rolls initiative at engagement start (d20 + modifier). Determines action order each round.
- **Attack rolls**: Attacker rolls d20 + attack modifier vs defender's Armor Class. Meet or beat AC to hit.
- **Damage**: On hit, roll damage die for the weapon/attack type + modifier.
- **Critical hits**: Natural 20 = crit. Double damage dice.
- **Critical miss**: Natural 1 = miss regardless of modifiers.
- **Attacks of Opportunity**: If a player attempts to flee while within melee range of an enemy, the enemy gets one free attack before the move resolves.
- **Saving throws**: Used for special attacks (poison, knockback, etc.) — d20 + relevant modifier vs a DC.

### Action Economy (per round)
- Each entity gets one action per round (attack, use item, attempt flee)
- Speed/agility stat influences initiative but not number of actions (keep it simple early)

### Stat Surface (entities)
- HP
- AC (Armor Class)
- Attack modifier
- Damage die + modifier
- Initiative modifier
- Speed (influences initiative modifier)
- Save modifiers (STR, DEX, CON)

---

## Line of Sight

### Approach
Tile-based dungeon means LOS is a tile-path problem, not a raycasting problem.

Use **Bresenham's Line Algorithm** on the tile grid:
- Trace a line from entity A to entity B
- If any tile along that line is a wall, LOS is blocked
- If the path is clear, LOS is established

### When LOS is Checked
- Every time the player moves (check against all entities on floor)
- Every time an enemy moves (check against player)
- LOS checks are server-side only

### LOS Range
- Entities have a sight radius in tiles
- LOS must be within range AND unobstructed to trigger engagement
- Player sight radius: **5 tiles**
- Enemy sight radius: **4 tiles**

---

## Dungeon & Floor Generation

### Approach
Server-side procedural generation. Client never sees the generation logic.

Use **BSP (Binary Space Partitioning)** for room generation:
- Recursively split floor space into partitions
- Place a room in each partition
- Connect rooms with corridors
- Guarantees all rooms are reachable

### Floor Data
- Each floor has a seed (stored in DB for reproducibility/debugging)
- Floor is a 2D tile grid
- Tile types: floor, wall, door, stairs-up, stairs-down
- Rooms are tracked as named regions with bounds
- Corridors connect rooms

### Fog of War
- Visibility states: hidden, explored, visible
- Visible: currently in player LOS
- Explored: previously seen but not currently in LOS (rendered darker)
- Hidden: never seen (not rendered)
- **Fog of war lives on each Player, not on the Tile.** Two players in the same room have independent fog. Tiles only carry their `Type`.
- Fog state is server-held, sent to client with each position update

---

## Rendering

- **View**: Top-down (not isometric)
- **Tile size**: Medium-small, 200–300 px range

---

## Character

- Players begin **classless** at run start. Class/archetype system will be added later.

---

## Data Model Overview

These are the core shapes (implemented in `Crawlers.Domain`). Implementation detail is in the code, but these shapes should not change without good reason.

**Conventions used in the implementation:**
- Tiny value types are `readonly record struct`: `Position`, `Bounds`, `DiceRoll`, `Tile`.
- Pure data records (with `init`-only props): `EntityStats`, `Item`, `Room`, `RunHistory`, `CombatEvent`.
- Mutable runtime objects (classes with `get; set;`): `Entity`, `Player`, `Floor`, `Session`, `CombatRound`, `CombatLog`.
- IDs are `Guid`. Timestamps are `DateTimeOffset`.
- 2D arrays use C# multidimensional `T[,]` form.

### Session
- id
- player_id
- floor_id
- current_floor_number
- mode (EXPLORATION | COMBAT | RESOLUTION)
- created_at

### Player (runtime state)
- id (player identity, persists across sessions)
- session_id
- position (x, y)
- stats (EntityStats — see below)
- inventory []
- fog_of_war (per-player VisibilityState[,])

### Floor
- id
- session_id
- floor_number
- seed
- width, height
- tile_grid (Tile[,])
- rooms []
- entities [] (enemies, items, interactables)

### EntityStats (shared by Player and enemy Entities)
- hp, max_hp
- ac (Armor Class)
- attack_mod
- damage (DiceRoll: count, sides, modifier)
- initiative_mod, speed
- **sight_radius** (in tiles — player default 5, enemy default 4)
- str_mod, dex_mod, con_mod (saving throw modifiers)

### Entity
- id
- floor_id
- type (enemy | item | npc)
- position (x, y)
- stats (if enemy)
- state (alive | dead | fled)

### CombatLog
- session_id
- floor_id
- rounds [] (each round is an ordered list of `CombatEvent`s)
- outcome (CombatOutcome: in_progress | player_won | player_fled | enemy_fled | player_died)
- started_at, ended_at

### CombatEvent
- actor_id, target_id (Guid? — present for combat actions, null for narrative)
- kind (CombatEventKind: narrative | hit | crit | miss | fumble | heal | death | loot | flee)
- damage (int? — present on hit/crit)
- description (human-readable log line)

### RunHistory
- player_id
- floors_cleared
- enemies_killed
- cause_of_death
- duration
- timestamp

---

## Project Build Order

Do not skip steps or reorder. Each step depends on the previous.

1. **Data models** — Define all core shapes in C#. No logic yet.
2. **Floor generation** — BSP algorithm produces a valid floor from data model. Prove with unit tests or console output. No rendering.
3. **Server + SignalR skeleton** — Session exists, player connects, state is held server-side and broadcast to client. No game logic yet.
4. **Renderer** — React + Pixi.js renders tile grid, fog of war, player position from server state.
5. **Player movement** — Client sends directional input, server validates, updates position, broadcasts. LOS + fog of war updates on each move.
6. **Entity placement + LOS checks** — Enemies exist on the floor. LOS evaluated on player move. Engagement triggers when conditions met.
6.5. **Dockerize backend** — Multi-stage Dockerfile, docker-compose with Postgres, env-driven config (no hardcoded secrets/origins). Infrastructure only — no game logic.
7. **Combat system** — Auto-battle resolution, round logging, state transitions, outcome handling.
8. **Items + inventory** — Passive and active items, loot drops, inventory management.
9. **Persistence** — Run history, character state saved to Postgres.
10. **Polish + balancing** — Stat tuning, additional enemy types, additional floor variety.

---

## What Not To Do

- Do not put game logic in the client
- Do not build multiplayer yet — design for it, don't build it
- Do not start rendering before the data model and floor gen are solid
- Do not skip unit tests on floor generation — bad gen will corrupt everything downstream
- Do not use any existing IP (DCC, D&D proper nouns, etc.)

---

## Resolved Design Questions

- **View**: Top-down
- **Stat names**: STR/DEX/CON (kept as-is)
- **Tile size**: 16 px native (0x72 atlas), rendered at 2×–3× via `worldContainer.scale`
- **Sight radius**: Player 5 tiles, enemies 4 tiles
- **Class system**: Classless at start; classes added later
