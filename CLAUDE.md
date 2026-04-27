# CLAUDE.md — Project Bible

## What This Is

A real-time co-op multiplayer dungeon crawler. Built to be a portfolio-quality project demonstrating server-authoritative game architecture, procedural generation, and D&D-adjacent combat systems.

The single-player path still works (a "session" is a one-player room) but the architecture is now multi-player end-to-end: code-based lobby, multi-floor session state, shared fog of war, per-player descent, simultaneous combats with friendly fire disabled, dead-body corpses, and spectator mode with cross-floor camera follow.

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
- [x] **Combat juice (server-side payload + client anims).** `CombatEvent` carries structured `Kind`/`ActorId`/`TargetId`/`Damage`; renderer plays per-event animations during combat: lunge + red flash on Hit, heavier flash + camera shake on Crit, sidestep dodge on Miss, jitter on Fumble, green pulse on Heal. Killing-blow Hit animates before the dying enemy sprite is destroyed.

### Multiplayer phase progress (see `MULTIPLAYER.md` for the full plan)

- [x] **Step 1 — Lobby system (server).** `LobbyHub` + `LobbyManager` with private code-only rooms. `CreateRoom`, `JoinRoomByCode` (handles late-joiners by routing them straight into the running session), `LeaveRoom`, `StartGame`. Lobbies cap at 4. `LobbyCodeGenerator` produces 6-char codes; lobby records persist after StartGame so late joiners see `AlreadyStarted` instead of `NotFound`.
- [x] **Step 2 — Lobby UI (client).** Asset-preload screen → lobby connect screen → menu (Create / Join by code) → in-room view with member list + Start. `App.tsx` orchestrates phases; `Lobby.tsx` renders. `GameStarting` event tears down the lobby connection and `Game.tsx` opens a fresh `/game` connection with the session id and the player id learned from the lobby.
- [x] **Step 3 — Multi-player session state (server).** `Session` is now metadata-only; `SessionState` holds `IReadOnlyList<Player>` keyed by id, `Dictionary<int, Floor>` keyed by floor number, and a `Dictionary<int, VisibilityState[,]>` for per-floor shared fog. `PlayerStartState` is a per-player initialization value object so the future continuation phase can drop a returning player onto whatever floor their saved state pinned them to without changing the API. `AddPlayerToSession` handles late joiners; idempotent for the same player id.
- [x] **Step 4 — Render other players (client).** `OtherPlayerDto` (id, x/y, hp/maxHp, inCombat). `DungeonRenderer` ticks teammate sprites and tweens them between tiles, with a name label `Player ABCD` (4-char id prefix) and a ⚔ suffix when they're fighting.
- [x] **Step 5 — Movement conflict resolution (server).** `MovementService` blocks moves into a tile occupied by another *living* player. Dead teammates don't block (corpses are walkable per spec). All mutations sit behind `SessionState.SyncRoot`, so two simultaneous Move invocations serialize and the second sees the first's new position.
- [x] **Step 6 — Shared fog of war (server).** Fog is one `VisibilityState[,]` per floor, owned by the session. `FieldOfView.RecomputeForFloor(floor, fog, players)` runs the union of every player on that floor's LOS — late joiners contribute their cone without demoting tiles a teammate can still see. Per-player fog deliberately removed from `Player`.
- [x] **Step 7 — Multi-player combat (server).** `ActiveCombat` carries `ParticipantPlayerIds`, `InitiativeOrder`, per-player `ParticipantOutcomes`, `FleeRequested` set, and a `UseItemRequested` queue. `CombatService.Start` rolls initiative for the whole cohort + the enemy; `AddPlayer` slots a late joiner at the back of the order without reshuffling. Friendly fire disabled — `EnemyTurn` only targets `ParticipantPlayerIds`. `GetCombatByEnemy` filters out finalized combats so a stale dead one doesn't shadow a fresh engagement on the same enemy.
- [x] **Step 8 — Combat state per-player (client).** `Player.Mode` is per-player (Exploration / Combat / Resolution). The `SnapshotMapper` builds a per-player snapshot — one player can be in Combat while a teammate explores or has died on another floor. The client reads `snapshot.mode` rather than a session-wide flag.
- [x] **Step 9 — Per-player floor descent (server).** `DescendService.TryDescend` is per-player: it checks that *this player* isn't in combat, generates the next floor lazily on first visit (deterministic seed `InitialSeed + (floorNumber - 1)`), recomputes shared fog on both source and destination floors, and BFS-spawns the descender at a free tile near the destination's stairs-up if a teammate has already parked there.
- [x] **Step 10 — Dead bodies.** `EntityType.Corpse` plus `Entity.PlayerId`. `CombatService.MarkPlayerDied` drops a Corpse entity at the death tile, persists for the run, doesn't block movement (filtered out of the player-collision check). Renderer draws a distinct corpse sprite. Persistence: a `corpses` table mirrors the in-memory drop, indexed on `(FloorNumber, X, Y)` so the future continuation phase can query "what corpses live on floor N?" without a schema change.
- [x] **Step 11 — Spectator mode with death delay.** `Player.SpectatorTargetId` + `GameHub.SetSpectatorTarget`. `SnapshotMapper` substitutes the target's floor/position/fog/combat into the dead player's snapshot so the camera follows survivors across descent boundaries; if the target dies or disconnects the binding clears server-side and the client re-prompts. `SpectatorOverlay.tsx` shows the 3-second death pause, then a picker, then a follow-banner with Switch.
- [x] **Step 12 — Disconnect & reconnect handling.** `OnDisconnectedAsync` clears the connection map entry but leaves the player record, position, HP, and inventory untouched on `SessionState`. `JoinSession(sessionId, playerId)` is the reconnect path — caller asserts they own that player id (the lobby handed it to them). `SessionBroadcaster` skips players with no registered connection. Disconnected players are filtered out of the spectator picker.
- [x] **Step 13 — Run end conditions.** `RunOutcome` enum (today: `PartyWiped`; reserved for future continuation outcomes). `SessionState.Outcome` + `EndedAt` + `EndRun()` (idempotent state transition). `RunEndService.CheckAndApply` runs inside the `CombatRunner`'s round lock — when every player is in `Resolution`, the run ends; disconnected-but-alive players keep it going (Mode, not connection, drives the check). `Player` carries `DiedAt`, `CauseOfDeath`, `DeepestFloorReached`. The snapshot mapper attaches an identical `RunSummaryDto` to every viewer once the run is over (party totals + per-player rows). Client `RunSummary.tsx` is a full-screen overlay that suppresses HUD / inventory / spectator UI; existing per-player run history rows already cover the per-death persistence the spec calls for. 258 tests passing.

---

## Solution Layout

```
crawlers/
├── CLAUDE.md
├── MULTIPLAYER.md                  (multiplayer phase plan — sequential build steps)
├── VISUAL_POLISH.md
└── server/
    ├── Crawlers.slnx
    └── src/
        ├── Crawlers.Domain/        ← shapes only, no logic
        │   ├── Enums/              (GameMode, EntityType incl. Corpse, LobbyStatus, …)
        │   └── Models/             (Player, Session, LobbyRoom, LobbyMember, …)
        ├── Crawlers.Generation/    ← BSP floor generator + entity placement + ASCII renderer
        ├── Crawlers.Server/        ← ASP.NET Core + SignalR hubs
        │   ├── Hubs/               (GameHub, LobbyHub, IGameClient, ILobbyClient, SessionBroadcaster)
        │   ├── Lobbies/            (LobbyManager, LobbyState, LobbyCodeGenerator, outcomes)
        │   ├── Sessions/           (SessionManager, SessionState, ActiveCombat, PlayerStartState, AdjacentSpawn)
        │   ├── Logic/              (MovementService, EngagementService, CombatService, CombatRunner, DescendService, FieldOfView, …)
        │   ├── Persistence/        (CrawlersDbContext, RunHistory + Corpse services, Migrations/)
        │   └── Contracts/          (DTOs + SnapshotMapper + LobbyMapper)
        └── ../tests/Crawlers.Tests/ ← xUnit; Domain + Generation + Server + Lobbies

crawlers/client/                    ← React + TS + Pixi.js v8 + SignalR client
├── vite.config.ts                  (host: true; proxies /game and /lobby → localhost:5238 with ws:true)
├── public/assets/dungeon/          (0x72 Dungeon Tileset II + assets.json manifest)
└── src/
    ├── api/                        (signalr.ts, lobby.ts, types.ts — TS mirrors of server contracts)
    ├── game/                       (DungeonRenderer, DungeonView, assets.ts, tileColors)
    ├── ui/                         (Hud, CombatLog, Inventory, MobileControls, Lobby, SpectatorOverlay)
    ├── App.tsx                     (asset preload → lobby phase machine → spawn Game on GameStarting)
    └── Game.tsx                    (per-game /game connect, key handling, snapshot → render)

crawlers/                           ← container infrastructure
├── docker-compose.yml              (postgres:16-alpine + server, env-driven)
├── .env.example                    (override defaults; .env is gitignored)
├── .gitignore
└── server/
    ├── Dockerfile                  (multi-stage; aspnet:9.0 runtime, non-root, EXPOSE 8080)
    └── .dockerignore
```

Hub endpoints: `/lobby` and `/game` (SignalR). Health check: `/health`. Server: `localhost:5238` (host) → `8080` (container). Client dev: `localhost:5173` (Vite, all interfaces).

### Configuration policy (Step 6.5)
- All environment-specific values flow through ASP.NET Core configuration. Override via env: `Cors__AllowedOrigins`, `ConnectionStrings__DefaultConnection`, `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`.
- `appsettings.json` carries dev-friendly defaults only — no secrets, no real connection strings.
- Compose composes the connection string from `POSTGRES_*` env vars; the server reaches Postgres via the compose-network alias `postgres:5432`.

Gameplay logic may be promoted out of `Crawlers.Server/Logic/` into a dedicated `Crawlers.Logic` project if a second host (e.g. headless test runner, separate match server) ever needs to consume it. Persistence already lives under `Crawlers.Server/Persistence/` rather than its own project — splitting it out is a refactor, not a redesign.

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

### Multiplayer (now built — see `MULTIPLAYER.md`)
The original architecture deliberately modelled sessions as rooms from day one even with one player. Multiplayer is now wired up end-to-end and most of the work was exposing existing server-held state, not adding new systems.

What multiplayer adds:
- Two SignalR hubs: `/lobby` for matchmaking (code-only joins, 4-player cap), `/game` for the running session.
- A session can hold 1–4 players spread across multiple floors at once. Floors are generated lazily and shared (deterministic seed per floor number).
- Per-player snapshots — every connected player gets a snapshot tailored to their floor, FOV, and combat. The `SessionBroadcaster` sends one filtered snapshot per connected player rather than a single group broadcast.
- `SessionState.SyncRoot` serializes every mutation (Move, Descend, UseItem, Flee, runner ticks, lobby→session bridge) so concurrent intent collapses into a deterministic order.

What is **not** in scope for the multiplayer phase (continuation phase work):
- Cross-run save state, voluntary quit that preserves a run, corpse-run XP recovery. Touch points are flagged in the multiplayer spec; build with awareness, do not implement.

### No Client-Side Game Logic
- Do not calculate movement validity on the client
- Do not calculate LOS on the client
- Do not resolve combat on the client
- The client may do *predictive rendering* for smoothness later, but never authoritative logic

---

## Game Modes (State Machine)

The game operates as a clear state machine. **Mode is per-player**, not per-session — one player can be in Combat while a teammate explores or has died on another floor.

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
- If enemy flees: that player returns to EXPLORATION, enemy entity removed or marked fled
- If player flees: that player returns to EXPLORATION (others stay in the fight)
- If enemy dies: loot resolved server-side, surviving participants flip back to EXPLORATION; a corpse drops, an item may drop at the enemy tile
- If a player dies: their `Mode` is pinned to RESOLUTION, a Corpse entity is dropped at their tile, and their teammates keep playing. The run as a whole only ends when **every** player is in RESOLUTION (Step 13).

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
- Visible: currently in any party member's LOS
- Explored: previously seen but not currently in LOS (rendered darker)
- Hidden: never seen (not rendered)
- **Fog of war is shared per floor**, owned by the session: `Dictionary<int, VisibilityState[,]>` keyed on floor number. `FieldOfView.RecomputeForFloor` runs the **union** of every player on that floor's LOS. (The earlier per-player design was discarded in Step 6 of the multiplayer phase — kept the docstring history but the new contract is shared.)
- A player descending to a new floor reveals it independently — fog state is per-floor, so floor 5's fog stays Hidden until someone walks there.
- Tiles only carry their `Type`. Fog state is server-held, sent to client embedded in each snapshot.

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

### Session (Domain.Models.Session)
- id, created_at — that's it. Just metadata. Runtime state lives on `SessionState` (server-only).

### SessionState (Crawlers.Server/Sessions/SessionState.cs)
- Players (1..4), keyed list
- Floors — `Dictionary<int, Floor>` (multiple floors held at once for split parties)
- Fogs — `Dictionary<int, VisibilityState[,]>` (shared fog per floor)
- ActiveCombats — per-participant pointer to the shared `ActiveCombat`
- Connections — playerId → SignalR connectionId (or absent for disconnected)
- InitialSeed — floor N derives its seed from `InitialSeed + (N - 1)`
- EnemiesKilled — session-wide tally for run history
- SyncRoot — lock object held during every mutation

### Player (runtime state)
- id, session_id
- position (x, y)
- current_floor_number
- mode (per-player: EXPLORATION | COMBAT | RESOLUTION)
- stats (EntityStats — see below)
- inventory []
- spectator_target_id (set when this dead player is following a teammate; cleared when target dies/disconnects)

### Floor
- id, session_id, floor_number
- seed
- width, height
- tile_grid (Tile[,])
- rooms []
- entities [] (enemies, items, corpses)

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
- type (enemy | item | npc | corpse)
- position (x, y)
- stats (if enemy)
- state (alive | dead | fled)
- item (if Type == Item — the carried Item)
- player_id (if Type == Corpse — the fallen player)

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

### RunHistory (Persistence/RunHistoryEntry.cs → `run_history` table)
- player_id, session_id, seed
- outcome (today: "died"; later: "quit", "won")
- cause_of_death
- deepest_floor, enemies_killed
- final_hp, final_max_hp, inventory_count
- started_at, ended_at

One row per **player death**, not per session — `(player_id, session_id)` together identifies a participant's run within a session. Indexed on `player_id` and `ended_at`.

### Corpses (Persistence/CorpseEntry.cs → `corpses` table)
- player_id, session_id
- floor_number, x, y
- died_at, cause_of_death

One row per death, mirrors the in-memory Corpse Entity. Indexed on `(FloorNumber, X, Y)` so the future continuation phase can query "what corpses live on floor N?" cheaply across runs.

### Lobby (Domain.Models.LobbyRoom)
- id, code (6-char alphanumeric, normalized uppercase)
- host_player_id, max_players (4)
- status (Waiting | InGame)
- session_id (set on Start; surfaced to late-joiners so they hop straight into `/game`)
- members [] (each: player_id, connection_id, joined_at)
- created_at

---

## Project Build Order

Do not skip steps or reorder. Each step depends on the previous.

The original 10-step single-player order has been delivered (1–10 plus 6.5 Docker, plus Visual Polish phase 1, plus Combat juice). The current active build order lives in `MULTIPLAYER.md` — steps 1 through 12 are shipped, step 13 (run-end conditions) is the active step.

Original single-player order, for reference:

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
- Do not skip unit tests on floor generation — bad gen will corrupt everything downstream
- Do not use any existing IP (DCC, D&D proper nouns, etc.)
- Do not implement continuation/save state in this phase — only build with awareness of where it will plug in (see `MULTIPLAYER.md` "Continuation Integration")
- Do not add PvP — players in the same session are co-op only; friendly fire is explicitly disabled
- Do not add public room browsing — code-only joins (locked decision in `MULTIPLAYER.md`)
- Do not break the per-player snapshot contract — every connected player must receive a snapshot built from *their* perspective (their floor, their FOV, their combat). The `SessionBroadcaster` enforces this; resist the temptation to fall back to a single SignalR group broadcast.

---

## Resolved Design Questions

- **View**: Top-down
- **Stat names**: STR/DEX/CON (kept as-is)
- **Tile size**: 16 px native (0x72 atlas), rendered at 2×–3× via `worldContainer.scale`
- **Sight radius**: Player 5 tiles, enemies 4 tiles
- **Class system**: Classless at start; classes added later
- **Multiplayer locked decisions** (full table in `MULTIPLAYER.md`): code-only joins; 4-player cap; shared fog of war; per-player floor descent; run ends only when all players are dead; friendly fire disabled; corpses persist for the run, don't block movement; 3-second death pause before spectator mode; reconnect to exact pre-disconnect floor and tile.
