# AI_BEHAVIOR.md — Phase Plan

## What This Is

The plan to give enemies basic spatial awareness and hunting behavior. Currently enemies are placed and remain static until the player walks into engagement range — combat is entirely "player-initiates" because the only thing that triggers `EngagementService.FindEngagement` is a successful player move. After this phase, enemies that can see a player will pursue them on a slow tick, creating natural chase dynamics, forcing the player to think about kiting and engagement timing, and making the dungeon feel like the enemies want to do something to you instead of just standing around politely.

Belongs as a separate phase from `CONTENT_AND_DEPTH.md` because it's a real gameplay shift (the dungeon changes feel), not a content addition.

---

## Philosophy

**Server-authoritative still.** Pathfinding and AI ticks run entirely on the server. Clients render results. No client-side prediction of enemy movement.

**Predictable beats clever.** "Sees you, walks toward you" is a model the player can hold in their head. No behavior trees, no aggro tables, no sneaky flanking. If a player asks "why is that mob doing that?" the answer is always one of three sentences.

**Cheap per tick.** Default is one tile per AI tick at ~700ms. That's deliberate enough to feel spooky and slow enough that the per-tick CPU budget doesn't matter at our scale. Bound the path search by sight radius so it can't accidentally explore a whole floor.

**Static idle.** Enemies don't wander when no target is visible. Random wander makes the dungeon feel busy in a way that contradicts the "spooky empty floor" tone — leave it to a future phase if it's ever wanted.

---

## Locked Design Decisions

These were decided in the planning phase. Don't deviate without revisiting.

| Decision | Choice                                                                                                                                                         |
|---|----------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Trigger | Enemy has line-of-sight to a player within `Stats.SightRadius` (Bresenham, walls block)                                                                        |
| Behavior | Move one tile toward the closest visible player per AI tick                                                                                                    |
| Tick rate | ~700ms (slower than combat's 900ms — chases feel deliberate, not frantic)                                                                                      |
| Path search | BFS on the tile grid, capped at `sightRadius + 2` cells from origin                                                                                            |
| Recompute | Only when target tile differs from the cached one OR the cached next-step is now blocked                                                                       |
| Loss of sight | Enemy keeps walking toward last-seen tile for 3 ticks (~2 seconds), then stops                                                                                 |
| Idle | Stationary. No random wander.                                                                                                                                  |
| Engagement | When the AI move lands an enemy at Chebyshev ≤ 1 of a player, fire the same `EngagementService.Engage` the player-move path uses                               |
| Combat | Enemies currently in combat skip AI ticks — `CombatRunner` owns their behavior                                                                                 |
| Mimics | Skip until opened. Mimics start as `EntityType.Chest` so the AI loop ignores them naturally; once `ChestService` swaps in the Mimic enemy, normal rules apply. |
| Bosses | Stairwell bosses are room-bound — never path outside `BossRoomBounds`. They still chase inside the room.                                                       |
| Doors | Closed doors block paths. Open doors traversable. The locked boss door blocks both directions once the players seal themselves in.                             |
| Enemy traffic | Two enemies wanting the same tile: stable iteration order (by `Entity.Id`) resolves; the loser stays this tick.                                                |
| Multiplayer targeting | Closest player by Chebyshev. Tie broken by `Player.Id` lexicographic order.                                                                                    |
| Multi-floor | AI ticks run only on floors that have at least one live player on them.                                                                                        |
| AI state lifetime | Per-session, per-enemy. Not persisted. Resets when the floor unloads.                                                                                          |

---

## Tech Notes

| Concern | Approach |
|---|---|
| Pathfinding | BFS on the tile grid. A* is overkill for an unweighted grid where adjacency cost is uniform. Move cost = 1 per tile. |
| Path cache | Each enemy carries `LastTargetTile` + `CachedPath` (a list of tiles). Reuse if the target hasn't moved and the next step is still walkable. |
| Walkability | Reuse `MovementService.IsWalkable` for the tile-type predicate; layer on a `IsTilePassableForEnemy(floor, target)` that also checks entity occupancy + door state + boss-room containment. |
| Enemy AI state | Append-only fields on `Entity`: `LastSeenPlayerTile`, `GiveUpTicksRemaining`. Default null / 0. |
| Tick service | New `EnemyAiRunner` hosted service, mirrors `CombatRunner`'s shape — iterate active sessions, batch by floor, hold `SessionState.SyncRoot` while applying moves, broadcast snapshot if anything moved. |
| Snapshot | No DTO change — enemy positions are already in `floor.entities`. The new fields stay server-side. |
| Tick budget | Goal: under 10ms per session per tick. With ~16 enemies × bounded BFS × per-floor scoping, this is comfortably reachable on the M-series mac dev box. |
| Determinism | Path tie-breaking is deterministic (BFS order + entity id ordering) so two runs of the same seed + actions produce the same chase. Useful for replay tests. |

---

## Build Order

Strictly sequential. Each step ships independently and improves the game on its own.

### Step 1 — Pathfinding utility
- New `Crawlers.Generation/Pathfinding/Bfs.cs`
- `Bfs.NextStep(grid, walkable, from, to, maxRadius)` returns the next tile, or null if unreachable inside the budget
- Pure unit tests with hand-built grids (no session state)
- ~80 lines + ~150 of tests

### Step 2 — Enemy passability rules
- `EnemyMovement.CanEnter(floor, enemy, target)` — floor tile, not stairs (enemies don't descend), not occupied by another alive entity, target's door state checked
- Tests cover door state, entity stacking, boss-room containment for room-bound enemies
- ~50 lines

### Step 3 — Sight + targeting helpers
- `EnemyAi.FindTarget(state, enemy, floor)` — returns the player to chase
  - Iterate live players on the floor
  - Bresenham LOS via existing `FieldOfView` helper
  - Within `Stats.SightRadius`
  - Closest by Chebyshev wins; tie by player.Id
- Falls back to `LastSeenPlayerTile` if grace remains; otherwise null
- ~70 lines + tests

### Step 4 — Enemy turn helper (no runner yet)
- `EnemyAi.TakeTurn(state, enemy, floor, dice)` — pure function
  - Find target → BFS → CanEnter → step
  - Update AI state (clears give-up on direct sight, decrements otherwise)
  - Returns true if the enemy moved
- Test against fixed scenarios: single chase, lost LOS recovery, blocked path
- ~80 lines

### Step 5 — `EnemyAiRunner` background service
- Hosted service, ~700ms tick
- Iterate `SessionManager._sessions`, hold each session's lock
- For each floor with live players: each non-combat, non-mimic-chest enemy takes a turn
- Broadcast snapshot if any enemy moved
- Mirrors the `CombatRunner` shape — copy/adapt the start/stop pattern
- ~120 lines

### Step 6 — Engagement from enemy-move
- After a successful AI step: if `enemy.Position` is now Chebyshev ≤ 1 of any live player, run the same `EngagementService.Engage` path the player-move flow uses
- Idempotent via `state.GetCombatByEnemy` — already handled by the existing engagement code
- ~30 lines

### Step 7 — Tests
- Chase: enemy at distance 4 spots player, paths over 4 ticks, lands adjacent, combat fires
- LOS lost mid-chase: enemy continues toward last-seen tile for 3 ticks, then stops
- Two enemies converging: same target tile resolved by stable order, no overlap
- Boss room-bound: BigSlug doesn't leave its room even with the door open
- Mimic-chest unopened: skipped by AI iteration
- Multiplayer: closer player targeted; switch on closer player death
- Smoke: 16-enemy floor under 10ms tick budget

### Step 8 — Tunables
- AI tick interval, sight-radius search cap, give-up grace count → constants in one config file (or extend `floor-scaling.json` with optional per-floor AI variants)
- ~20 lines

---

## Edge Cases (Locked Behavior)

### Enemy can't reach the player (separated by walls)
BFS returns null; enemy stays put. Useful so enemies behind locked doors don't pace forever.

### Two enemies want the same tile
Stable iteration order (by `Entity.Id`) resolves; loser stays this tick, retries next tick. No deadlock.

### Player descends mid-chase
The enemy's target leaves the floor; on the next AI tick, `FindTarget` returns null. Enemy walks toward `LastSeenPlayerTile` for the grace window, then stops.

### Boss room
Stairwell bosses don't path outside `BossRoomBounds`. Inside the room, they chase normally. The locked boss-door rule means once the room seals, the boss can't get out and the players can't get out — same as today.

### Mimic chest
Skipped by the AI loop because it's `EntityType.Chest`, not `Enemy`. After `ChestService.ResolveMimicOpen` swaps in the Mimic enemy, normal AI rules apply.

### AoO during chase
If the player flees an existing combat (per the existing flee path, which already triggers AoO), nothing here changes. The AI runner doesn't intervene — combat is owned by `CombatRunner` while it's active.

### Enemy currently in combat
Skipped. Combat ticks own the enemy's behavior. AI re-engages after combat ends if the player's still in sight.

### Enemy slept / disabled (future-proofing)
A future status effect like "Stun" could short-circuit `TakeTurn` before pathing. The Step 5 status-effect plumbing already gives us a place to hang that flag — not part of this phase.

### Empty floors
AI ticks skip floors with no live players. Floors that the party left behind go cold until someone returns.

### Performance falls off a cliff
Bound the BFS at `sightRadius + 1`. Cache paths. Skip floors. If ever a problem at scale, an A* with manhattan-distance heuristic is a drop-in replacement.

---

## Definition of Done

- Walking past an enemy turns it and starts the chase.
- Standing in an open room with multiple monsters causes them to converge on you.
- Closing a door behind you stops them at the door (until you re-open it).
- Bosses don't follow you out of the boss room.
- Enemies that lose LOS for too long give up and stop.
- AI tick CPU budget under ~10ms per session per tick on the dev box.
- The whole loop is testable in isolation — `Bfs`, `EnemyMovement`, `EnemyAi.FindTarget`, `EnemyAi.TakeTurn` are pure helpers; only `EnemyAiRunner` needs the hosted-service shell.

---

## Out of Scope (Explicitly Deferred)

- **Aggro from sound** (combat noise, spell casts) — needs an audibility system; future phase.
- **Friend-of-foe** (orcs attacking demons) — would force per-archetype faction rules; not worth it for a portfolio piece.
- **Pack tactics / flanking** — formation-aware AI is its own design effort.
- **Fleeing low-HP enemies** — interesting but inverts the chase model; defer.
- **Ranged attacks during pursuit** — ranged combat is out of scope for this whole project.
- **Random wander when idle** — contradicts the "static idle" decision; revisit only if playtests demand it.
- **Difficulty-tuned AI** (smarter enemies on deeper floors) — overlaps with `floor-scaling.json`'s difficulty curve; skip unless playtests show flat AI feels stale on cycle-2+ floors.
- **Pathfinding through breakable terrain / chests** — chests are walkable to AI today (they're entities, not tiles); leave it that way.

These are all good ideas. They belong in a later phase. Do not pull them into this one.

---

## Future Phase Hooks (Out of Scope, Architectured For)

- **Smarter targeting** (lowest-HP, last-attacker preference) — slot in at `FindTarget`.
- **Pack behavior** — coordinator service that nudges multiple enemies at once; can read the same per-enemy state.
- **Status-effect interaction** — if Stun / Slow / Charm ever land, they short-circuit or modify `TakeTurn` cleanly because it's a pure function.
- **AI difficulty knobs** — per-floor AI tick interval, per-archetype sight-radius, per-archetype give-up grace; all hookable into the existing `FloorScaling` shape without a schema change.
