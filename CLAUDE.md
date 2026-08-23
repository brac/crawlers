# CLAUDE.md — Project Bible

## What This Is

A real-time co-op multiplayer dungeon crawler. Portfolio-quality project demonstrating server-authoritative game architecture, procedural generation, and D&D-adjacent combat. Code-based lobby (1–4 players), multi-floor session state, shared fog of war, per-player descent, simultaneous combats with friendly fire disabled, dead-body corpses, teammate revive, spectator mode with cross-floor camera follow.

Enemies hunt: an AI tick gives sighted enemies pursuit, so combat is no longer purely player-initiated. Floors scale in difficulty by depth, with per-floor monster pools, weapon/consumable loot, chests (some are mimics), and lightweight status effects (bleed, poison).

The world is persistent: corpses, the canonical dungeon, and player identity survive across sessions. Live multiplayer stays party-scoped (room codes); persistence (corpses, heatmap, environmental memory) is world-scoped.

The game is **not** based on any existing IP. Tone, mechanics, and universe are original.

---

## Phase Status

- **Single-player core (Steps 1–10) + Docker (6.5):** shipped.
- **Visual polish phase 1 + Combat juice:** shipped.
- **Multiplayer (Steps 1–13):** shipped. Code-based lobby, shared fog, per-player descent, revive, spectator mode.
- **Persistent-world (Steps 1–5, 9–12):** shipped (Steps 6–8 — blood trails, icon placement, icon render — intentionally skipped per portfolio scope).
- **Content-and-depth:** shipped — floor scaling, monster pools, weapon/consumable loot, chests + mimics, status effects (bleed/poison), 4 authored floors with a placeholder Floor 4 boss. The Floor 4 capstone boss design is deliberately deferred.
- **Enemy AI (hunting/chase):** shipped. Sighted enemies pursue on a 700 ms tick. See `AI_BEHAVIOR.md`.
- **Teammate revive:** shipped. A living teammate adjacent to a dead teammate's corpse can pay HP to revive them.
- **Combat agency (action choices in combat):** planned, not started — no code yet.
- **Live deploy:** shipped. Running at <https://crawlers.brac.dev>; `/health` returns `{"status":"ok"}`. SignalR runs on plain HTTP inside the container, so any deployment must terminate TLS in front of it and forward the WebSocket upgrade.

For per-step detail, read `AI_BEHAVIOR.md` or `git log`. CLAUDE.md captures durable design — not build progress.

---

## Solution Layout

```
crawlers/
├── CLAUDE.md, README.md
├── AI_BEHAVIOR.md
├── docker-compose.yml, .env.example, .github/ (CI)
├── server/
│   ├── Crawlers.slnx
│   ├── src/
│   │   ├── Crawlers.Domain/        ← shapes only, no logic (Models/, Enums/)
│   │   ├── Crawlers.Generation/    ← BSP gen + placement + ASCII renderer
│   │   │   ├── BspFloorGenerator, BspNode, DoorPlacer, EntityPlacer
│   │   │   ├── EnemyTemplates, GenerationConfig, FloorAsciiRenderer
│   │   │   ├── Pathfinding/         (Bfs — enemy chase path search)
│   │   │   ├── Scaling/             (EnemyScaler, FloorScaling(Table), monster/loot pools)
│   │   │   └── Weapons/             (WeaponDefinition, WeaponRegistry)
│   │   └── Crawlers.Server/         ← ASP.NET Core + SignalR
│   │       ├── Program.cs
│   │       ├── Config/              (WeaponRegistryLoader + weapons.json, FloorScalingLoader + floor-scaling.json)
│   │       ├── Hubs/                (GameHub, LobbyHub, SessionBroadcaster, IGameClient, ILobbyClient)
│   │       ├── Lobbies/             (LobbyManager, LobbyState, LobbyCodeGenerator, LobbyOutcomes)
│   │       ├── Sessions/            (SessionManager, SessionState, ActiveCombat, PlayerStartState, AdjacentSpawn)
│   │       ├── Logic/               (Movement, Engagement, Combat(Runner)(Service), Descend, FieldOfView, RunEnd,
│   │       │                         EnemyAi(Runner)(Movement), ReviveService, ChestService, ItemEffects/Templates,
│   │       │                         StatusEffectHelper, FloorNameResolver, Dice, …)
│   │       ├── Persistence/         (CrawlersDbContext + factory, PlayerIdentity / RunHistory / Corpse / FloorWorld /
│   │       │                         WorldStats services + Null* fallbacks, Floor/Corpse records, WorldConstants, Migrations/)
│   │       └── Contracts/           (DTOs + SnapshotMapper + LobbyMapper)
│   └── tests/Crawlers.Tests/        ← xUnit (Generation, Logic, Sessions, Lobbies, Persistence, Contracts, TestSupport)
└── client/                          ← Vite + React + TS + Pixi.js v8 + SignalR
    ├── public/assets/dungeon/       (0x72 Dungeon Tileset II + assets.json manifest)
    └── src/
        ├── api/                     (signalr.ts, lobby.ts, types.ts — TS mirrors of server contracts)
        ├── game/                    (DungeonRenderer, DungeonView, assets.ts, tileColors.ts)
        ├── ui/                      (Hud, CombatLog, Inventory, MobileControls, Lobby, IdentitySetup,
        │                            SpectatorOverlay, RunSummary, WorldStats, FloorAnnouncer, FloorTitleCard,
        │                            CorpseTooltip, ReviveDialog)
        ├── dev/                     (SpriteProbe — atlas inspection tooling)
        ├── identity.ts              (localStorage UUID + username)
        ├── App.tsx                  (asset preload → identity → lobby phase machine → spawn Game)
        └── Game.tsx                 (per-game /game connect, key handling, snapshot → render)
```

Hubs: `/lobby`, `/game`. REST: `GET /api/world-stats`. Health: `/health`. Root `/` returns a liveness string. Server: `localhost:5238` (host) → `8080` (container). Client dev: `localhost:5173`.

### Layer rules
- **Domain** — data shapes only. No logic. No dependencies on other Crawlers projects.
- **Generation** — depends on Domain. Pure functions of `(seed, config)`. Produces `Floor` instances.
- All future server projects depend down the stack, never sideways or up.

### Configuration policy
- All environment-specific values flow through ASP.NET Core configuration. Override via env: `Cors__AllowedOrigins`, `ConnectionStrings__DefaultConnection`, `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`, `World__BaseSeed`.
- `appsettings.json` carries dev-friendly defaults only — no secrets, no real connection strings.
- Compose composes the connection string from `POSTGRES_*` env vars; the server reaches Postgres via the compose-network alias `postgres:5432`.
- When `ConnectionStrings:DefaultConnection` is empty, every persistence service falls back to a `Null*` in-memory implementation. Postgres is optional in dev; required in prod.

### Debug rendering policy
- ASCII is the only server-side debug renderer (`FloorAsciiRenderer`). It belongs to dev/test tooling.
- The server never emits anything intended for human eyes other than logs and ASCII.
- Real visual rendering is the client's job (React + Pixi.js).

### Visual policy
- Tileset: 0x72 Dungeon Tileset II v1.7. All sprite coordinates declared in `client/public/assets/dungeon/assets.json` — never hardcode atlas frame coords in TS.
- Native tile size 16 px; renderer applies integer scale (2× phones, 3× desktops) to `worldContainer`. Camera tracks the **tweened** player position, not the snapshot tile, so movement glides.
- Characters are `AnimatedSprite` (idle "breathing" + run cycle on a 250 ms ease-out tween). Items are `Sprite`.
- Combat anims are queued from structured `CombatEvent`s via a watermark (Hit/Crit/Miss/Fumble/Heal). Killing-blow sprites defer destruction until pending anims drain.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | React + Pixi.js v8 (rendering) + React (HUD) |
| Realtime | SignalR |
| Backend | C# / ASP.NET Core (net9.0) |
| Database | PostgreSQL + EF Core 9 (Npgsql) |
| Generation | C#, server-side only |

---

## Architecture Principles

### The Golden Rule
**The server owns all truth. The client owns nothing.**

- Client sends *intent*: move, use item, flee, descend.
- Server validates, mutates, broadcasts.
- Client re-renders.

This applies in single player too. No shortcuts that would need to be undone for multiplayer.

### Concurrency
- All session mutations sit behind `SessionState.SyncRoot`. Move, Descend, UseItem, Flee, runner ticks, and the lobby→session bridge serialize through one lock per session.
- Every connected player gets a snapshot built from *their* perspective (their floor, that floor's shared fog, their combat). `SessionBroadcaster` enforces this — resist falling back to a single SignalR group broadcast.

### No client-side game logic
- No movement validation, no LOS, no combat resolution on the client.
- Predictive rendering (tween smoothing) is fine. Authoritative logic is not.

### World vs. session scope
- **Live presence** (other players, combat, fog) is session-scoped. Two parties on the same canonical floor never see each other's living players.
- **Persistence** (canonical floors, corpses, heatmap, run history, player identity, world stats) is world-scoped. One world, populated forever by every player who ever played.

### Out of scope
- PvP / friendly fire.
- Public room browsing (code-only joins).
- Cross-run save state, voluntary quit-with-save, corpse-run XP recovery (continuation-phase work).

---

## Game Modes (per-player state machine)

```
EXPLORATION → ENGAGEMENT → COMBAT → RESOLUTION → EXPLORATION
```

**Mode is per-player.** One player can be in Combat while a teammate explores or has died on another floor.

- **Exploration** — free movement (WASD / arrows / mobile D-pad). LOS recomputed each move; engagement triggers on Chebyshev≤1 (`EngagementProximity`) + Visible-in-fog. Either a player move *into* an enemy or an AI move *into* a player fires the same `EngagementService.Engage`.
- **Combat** — auto-resolved in rounds. Player may use an item or attempt flee (AoO on flee within melee range). Movement otherwise locked.
- **Resolution** — combat ends. Survivor flips back to Exploration. Dead players pin to Resolution and drop a Corpse entity; teammates keep playing. The run as a whole ends only when **every** player is in Resolution.

---

## Combat

Auto-battler. Server resolves the entire exchange, so there is no input timing, no latency sensitivity, and no client input that can change a roll or its result. `CombatRunner` background task ticks every 900 ms per fighting session.

### D&D-adjacent rules (inspired-by, not 1:1)
- Initiative: d20 + mod at engagement start; whole cohort + enemy.
- Attack: d20 + atk mod vs AC. Meet or beat hits. Nat 20 = crit (double dice). Nat 1 = fumble (auto miss).
- Damage: weapon dice + mod.
- AoO: free attack from adjacent enemy when fleeing.
- Saves: STR / DEX / CON for special attacks (poison, knockback, …).
- One action per round (attack / use item / flee). Speed influences initiative, not action count.

### Stats (entities)
HP / max HP, AC, attack mod, damage (DiceRoll), initiative mod, speed, sight radius, save mods (STR/DEX/CON).

### Multi-player combat
`ActiveCombat` carries `ParticipantPlayerIds`, `InitiativeOrder`, per-player `ParticipantOutcomes`, `FleeRequested`, `UseItemRequested`. Late joiners slot at the back of the order without reshuffling. Friendly fire disabled — enemy turn only targets participants.

### Status effects
Lightweight, server-resolved, extensible (`StatusEffectKind`). Shipped: **Bleed** and **Poison** — applied on hit by certain enemies, tick damage over rounds via `StatusEffectHelper`. Keep the set small (don't add burn/slow/stun in this scope); the system is built to extend without rearchitecture.

---

## Enemy AI (hunting)

Server-authoritative pursuit. `EnemyAiRunner` background task ticks every **700 ms** (slower than combat's 900 ms — chases feel deliberate). Full design and locked decisions in `AI_BEHAVIOR.md`.

- **Trigger**: enemy has Bresenham LOS to a player within `Stats.SightRadius`. Idle enemies are stationary — no random wander.
- **Behavior**: `EnemyAi` picks the closest visible player (Chebyshev), `Bfs` (capped at `SightRadius + 2`) finds the next step, `EnemyMovement` advances one tile per tick toward it.
- **Loss of sight**: keeps walking toward `LastSeenPlayerTile` for `GiveUpGrace` (25) ticks, then stops.
- **Engagement**: an AI move landing at Chebyshev≤1 of a player fires the same `EngagementService.Engage` as a player move.
- **Skips**: enemies already in combat (CombatRunner owns them), unopened chests/mimics, and room-bound bosses leaving `BossRoomBounds`.

---

## Content & Loot

- **Floor scaling**: `FloorScaling` / `FloorScalingTable` (data-driven from `Config/floor-scaling.json`) sets per-floor monster pools, enemy stat scaling (`EnemyScaler`), and loot weights. 4 authored floors; `FloorScalingTable.For` wraps past the table cyclically, so descent is endless and each lap compounds HP/damage/gold multipliers on the reused base entry.
- **Enemy archetypes**: `EnemyArchetype` (e.g. caster/status-applier, large monster). Templates in `EnemyTemplates`.
- **Weapons**: data-driven registry (`Config/weapons.json` → `WeaponRegistry`). Weapons carry damage dice + mods; drop as loot weighted by floor.
- **Chests & mimics**: `ChestService` opens chests for loot. Some chests are mimics — they start as `EntityType.Chest` (AI ignores them) until opened, then `ChestService` swaps in the Mimic enemy and normal combat/AI rules apply.
- **Floor 4 boss**: deliberate placeholder. The multi-phase capstone (summons, phase transitions) is deferred to a dedicated future pass.

---

## Revive

Multiplayer-only. A **living** teammate standing adjacent to a dead teammate's corpse calls `GameHub.ReviveTeammate(corpsePlayerId)` → `ReviveService`. The reviver pays **20% of current HP** (min 1) to bring the dead player back. Neither player can be pushed below 1 HP — if the tax would kill the reviver, both end at 1 HP. The dead player must still be in spectator mode (Mode == Resolution, still connected). No cap on revives given or received; a 1-HP reviver is rejected outright (can't pay the tax).

---

## Line of Sight & Fog

- **Algorithm**: Bresenham's line on the tile grid, server-side only. Walls block.
- **Range**: Player sight radius **5** tiles. Enemies carry their own `Stats.SightRadius`, **3 to 5** depending on the template (`EnemyTemplates`).
- **Fog states**: Hidden / Explored / Visible. `Dictionary<int, VisibilityState[,]>` keyed on floor number, owned by the session.
- **Shared fog**: `FieldOfView.RecomputeForFloor` runs the **union** of every player on that floor's LOS — late joiners contribute their cone without demoting tiles a teammate can still see.
- New floors stay Hidden until someone descends. Tiles only carry `Type`; fog is server-held and embedded in each snapshot.

---

## Floor Generation

- BSP recursively partitions space → places a room per partition → connects rooms with corridors. Connectivity guaranteed.
- Tile types: floor, wall, door, stairs-up, stairs-down.
- Floors are **canonical** and world-scoped. One row per `floor_number` in the `floors` table holds seed, tile grid (bytea), rooms (jsonb), boss-room metadata, and a version stamp matching `WorldConstants.Version`.
- `IFloorWorldService` mints floors 1..`InitialFloorCount` at startup and lazy-mints deeper floors on first descent. Sessions clone the canonical grid + rooms into a per-session `Floor` so door bumps stay session-scoped.
- Seed: `World:BaseSeed + (floor_number - 1)` (default base `0x1afe5c3`).
- Bumping `WorldConstants.Version` wipes both the `floors` table and every corpse keyed to those floor numbers (regenerated coordinates wouldn't be coherent).

---

## Persistence Tables

Detail of column shapes lives in the `Persistence/` records — this list is the catalogue.

- **`players`** — persistent identity. PK is the UUID minted in the browser. Reused as `Player.Id`, `RunHistoryEntry.PlayerId`, `CorpseEntry.PlayerId` so cross-run queries join on a single column. Established at lobby connect via `LobbyHub.Identify(playerId, username)`; precondition for `CreateRoom` / `JoinRoomByCode` / `StartGame`.
- **`floors`** — canonical world dungeon (see Floor Generation).
- **`run_history`** — one row per player **death**, not per session. `(player_id, session_id)` identifies a participant's run. Indexed on `player_id` and `ended_at`.
- **`corpses`** — world-scoped, one row per death across every session ever. Carries frozen `player_username` + `killer_type` so renames don't rewrite headstones. Indexed on `(FloorNumber, X, Y)` (heatmap query) and `(PlayerId)`. Never deleted by gameplay — only by a `WorldConstants.Version` bump.

---

## Resolved Design Questions

- **View**: Top-down (not isometric).
- **Tile size**: 16 px native, 2×–3× via `worldContainer.scale`.
- **Sight radius**: Player 5; enemies 3–5 per template (enemy AI pursuit uses each enemy's own `Stats.SightRadius`).
- **Engagement proximity**: Chebyshev ≤ 1 (`EngagementProximity`).
- **Enemy AI tick**: 700 ms; give-up grace 25 ticks; BFS path capped at `SightRadius + 2`; no idle wander.
- **Stat names**: STR / DEX / CON.
- **Floor scope**: 4 authored floors with a placeholder Floor 4 boss (capstone design deferred); deeper floors cycle the table with compounding difficulty.
- **Status effects**: bleed and poison only in this scope.
- **Revive cost**: 20% of reviver's current HP, neither party below 1 HP.
- **Class system**: classless at run start; class/archetype system deferred.
- **Multiplayer locked decisions**: code-only joins; 4-player cap; shared fog of war; per-player floor descent; run ends only when all players are dead; friendly fire disabled; corpses persist for the run, don't block movement; 3-second death pause before spectator mode; reconnect to exact pre-disconnect floor and tile.
- **Persistence scope**: live presence is session-scoped, persistence is world-scoped.

---

## What Not To Do

- Don't put game logic in the client.
- Don't skip unit tests on floor generation — bad gen corrupts everything downstream.
- Don't use existing IP (DCC, D&D proper nouns, etc.).
- Don't implement continuation/save state in this phase. Build with awareness of where it would plug in.
- Don't add PvP. Co-op only; friendly fire is explicitly disabled.
- Don't add public room browsing.
- Don't break the per-player snapshot contract — every connected player must receive a snapshot built from *their* perspective. `SessionBroadcaster` enforces this; resist falling back to a single group broadcast.
- Don't hardcode atlas frame coordinates in TS — declare them in `assets.json`.
- Don't query a global player list for live presence — `state.PlayersOnFloor(...)` keeps two parties on the same canonical floor invisible to each other. `CrossSessionPresenceTests` locks the contract.

---

## Devlog artifact (required at phase completion)

Every completed phase MUST produce a devlog draft before the phase can be marked complete. The devlog is a phase artifact with the same standing as code, tests, and benchmarks: **the reviewer gates on it.**

### File

- Path: `devlog/phase-{N}-{short-slug}.md` (e.g. `devlog/phase-3-collision-rework.md`)
- Format: **plain CommonMark + YAML frontmatter. Never MDX. No HTML. No JSX. No components.** Embeds are added later by a human.
- One file per phase. Do not edit previous phases' devlog files.

### Frontmatter (all fields required unless marked optional)

```yaml
---
title: ""            # Post title. Specific and concrete, not generic.
                     # GOOD: "Rebuilding particlr's collision pass for 2,500 sprites"
                     # BAD:  "Phase 3 Update"
date: YYYY-MM-DD     # Date the phase completed
project: "crawlers"
phase: N             # Integer phase number
tags: []             # 2-5 lowercase tags, e.g. [pixijs, performance, collision]
draft: true          # Always true. Never set false. Publishing is a human act.
summary: ""          # One sentence, <160 chars. Used for cards, OG, RSS.
repo_ref: ""         # Commit SHA or tag this post describes (the phase-completion commit)
decisions:           # Every locked decision made this phase. Empty array if none.
  - what: ""         # The decision, one line
    why: ""          # The reason it won, one line
    alternatives: [] # What was rejected, as strings
benchmarks:          # Every measured number this phase. Empty array if none.
  - metric: ""       # e.g. "frame time @ 2500 sprites"
    value: ""        # e.g. "3.1ms"
    target: ""       # e.g. "<4.16ms (240Hz budget)" — the stop condition it satisfied
---
```

### Body structure

Write these sections, in this order, using `##` headings:

1. **`## What shipped`** — What exists at the end of this phase that didn't exist before. Concrete: features, systems, files. 2-4 paragraphs max.
2. **`## Decisions`** — Prose context for the frontmatter `decisions` array. For each: the situation, the options actually considered, why the winner won. This is the most valuable section — do not compress it to a table restatement.
3. **`## What broke`** — Failures, dead ends, and reverted approaches from this phase. Be specific: what was tried, what the failure looked like (error, artifact, bad numbers), what the fix or retreat was. If genuinely nothing broke, write one line saying so — do not invent drama.
4. **`## Numbers`** — Prose context for the `benchmarks` array: how measurements were taken, what moved since last phase, anything surprising.
5. **`## Next`** — 2-3 sentences on what the next phase targets. No roadmap essays.

### Voice and content rules

- **Capture raw material, not marketing.** The human editor adds voice and opinion. Your job is to make sure nothing interesting is lost: the exact error message, the number before and after, the alternative that almost won.
- **Past tense, first person plural is fine ("we moved the broadphase to...").** No exclamation marks. No "excitingly", "delightfully", "robust", "seamless", "blazing".
- **Every claim of improvement needs a number** from `benchmarks` or a `repo_ref`-reachable diff. No unquantified "significantly faster".
- **Name the failures honestly.** "What broke" with real content is what makes these posts worth reading. A phase with zero failures and zero rejected alternatives will be treated as an incomplete draft by the reviewer.
- Length target: 400-900 words of body. Below 400 usually means decisions/failures were dropped; above 900 usually means changelog padding.
- Do not reference internal orchestration (agent roles, prompts, gate mechanics) unless the phase was *about* the agent workflow itself.
- Do not include code blocks longer than ~15 lines; link to the `repo_ref` instead. Short, load-bearing snippets are fine.

### Reviewer gate checklist (devlog)

The reviewer MUST verify before phase sign-off:

- [ ] File exists at `devlog/phase-{N}-{slug}.md` and frontmatter parses as YAML
- [ ] All required frontmatter fields present; `draft: true`; `repo_ref` points at a real commit/tag in this repo
- [ ] Every locked decision from this phase's spec/decision log appears in `decisions`
- [ ] Every benchmark that gated this phase appears in `benchmarks` with the actual measured value
- [ ] All five body sections present in order
- [ ] "What broke" is non-empty or explicitly states nothing broke
- [ ] No superlative filler; no improvement claims without numbers; no MDX/HTML/JSX
- [ ] 400-900 words of body

A devlog that fails any item blocks phase completion, same as a failing benchmark.
