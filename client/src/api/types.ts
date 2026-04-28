// Mirrors of server contracts. Numeric values must match the C# enum order
// in server/src/Crawlers.Domain/Enums/.

export const TileType = {
  Floor: 0,
  Wall: 1,
  Door: 2,
  StairsUp: 3,
  StairsDown: 4,
  OpenDoor: 5,
  LockedDoor: 6,
} as const;
export type TileType = (typeof TileType)[keyof typeof TileType];

export const GameMode = {
  Exploration: 0,
  Combat: 1,
  Resolution: 2,
} as const;
export type GameMode = (typeof GameMode)[keyof typeof GameMode];

export const CombatOutcome = {
  InProgress: 0,
  PlayerWon: 1,
  PlayerFled: 2,
  EnemyFled: 3,
  PlayerDied: 4,
} as const;
export type CombatOutcome = (typeof CombatOutcome)[keyof typeof CombatOutcome];

export const VisibilityState = {
  Hidden: 0,
  Explored: 1,
  Visible: 2,
} as const;
export type VisibilityState = (typeof VisibilityState)[keyof typeof VisibilityState];

export const MoveDirection = {
  North: 0,
  South: 1,
  East: 2,
  West: 3,
} as const;
export type MoveDirection = (typeof MoveDirection)[keyof typeof MoveDirection];

export const EntityType = {
  Enemy: 0,
  Item: 1,
  Npc: 2,
  Corpse: 3,
} as const;
export type EntityType = (typeof EntityType)[keyof typeof EntityType];

export const ItemEffect = {
  None: 0,
  Heal: 1,
  AttackBonus: 2,
  DefenseBonus: 3,
} as const;
export type ItemEffect = (typeof ItemEffect)[keyof typeof ItemEffect];

export const CombatEventKind = {
  Narrative: 0,
  Hit: 1,
  Crit: 2,
  Miss: 3,
  Fumble: 4,
  Heal: 5,
  Death: 6,
  Loot: 7,
  Flee: 8,
} as const;
export type CombatEventKind =
  (typeof CombatEventKind)[keyof typeof CombatEventKind];

export interface ItemDto {
  id: string;
  name: string;
  description: string | null;
  isConsumable: boolean;
  effect: ItemEffect;
  effectValue: number;
}

export interface EntityDto {
  id: string;
  type: EntityType;
  name: string;
  x: number;
  y: number;
  // ISO 8601 timestamp; set on Corpse entities, drives Step 4 visual
  // aging on the client. Null for entity types the renderer doesn't fade.
  diedAt: string | null;
  // Step 5 tooltip metadata — Corpse entities only. Username is frozen
  // at the moment of death; killerType is the archetype tag ("Husk", …)
  // or null for non-combat deaths / legacy rows.
  username: string | null;
  killerType: string | null;
}

export interface RoomDto {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface TileHeatDto {
  x: number;
  y: number;
  count: number;
}

export interface FloorSnapshotDto {
  width: number;
  height: number;
  tiles: number[]; // row-major: tiles[y * width + x]
  visibility: number[]; // VisibilityState values, same row-major indexing
  rooms: RoomDto[];
  entities: EntityDto[]; // server-side fog filtering — only Visible enemies appear here
  // Step 9 — sparse list of (x, y, count) entries for tiles where deaths
  // have accumulated. Drives environmental tile-tinting in the renderer;
  // not exposed as a UI overlay.
  heatmap: TileHeatDto[];
  // Step 12 — bleak announcer line for the floor. Same string for every
  // snapshot of this floor in this session; the client detects floor
  // change to fade it in for a few seconds.
  flavor: string | null;
}

// Step 12 — public stats served by GET /api/world-stats. All counts are
// over the entire history of the persistent world. Nullable fields are
// null when the world hasn't accumulated enough data yet.
export interface KillerStat {
  killer: string;
  count: number;
}
export interface DeadliestTile {
  floorNumber: number;
  x: number;
  y: number;
  count: number;
}
export interface DeadliestPlayer {
  username: string;
  count: number;
}
export interface WorldStatsDto {
  totalPlayers: number;
  totalDeaths: number;
  deepestFloorReached: number;
  survivalRatePercent: number;
  averageFloorAtDeath: number;
  mostCommonKiller: KillerStat | null;
  deadliestTile: DeadliestTile | null;
  mostFallenPlayer: DeadliestPlayer | null;
}

export interface PlayerSnapshotDto {
  id: string;
  username: string;
  x: number;
  y: number;
  hp: number;
  maxHp: number;
  inventory: ItemDto[];
}

export interface CombatEventDto {
  actorId: string | null;
  targetId: string | null;
  kind: CombatEventKind;
  damage: number | null;
  description: string;
}

export interface CombatRoundDto {
  number: number;
  events: CombatEventDto[];
}

export interface CombatLogDto {
  // Stable id used by the renderer to maintain a per-combat event watermark.
  id: string;
  outcome: CombatOutcome;
  rounds: CombatRoundDto[];
}

export interface OtherPlayerDto {
  id: string;
  username: string;
  x: number;
  y: number;
  hp: number;
  maxHp: number;
  // True while this teammate is engaged in combat — the renderer shows a
  // ⚔ suffix on their name so non-participants can see at a glance who's
  // fighting.
  inCombat: boolean;
}

export interface SpectatableTargetDto {
  id: string;
  username: string;
  floorNumber: number;
  inCombat: boolean;
}

// Step 13 — terminal outcome stamped on the session once every player is in
// Resolution (or, in the future, once a continuation-phase quit fires).
export const RunOutcome = {
  PartyWiped: 0,
} as const;
export type RunOutcome = (typeof RunOutcome)[keyof typeof RunOutcome];

export interface RunSummaryPlayerDto {
  playerId: string;
  username: string;
  finalFloor: number;
  deepestFloor: number;
  finalHp: number;
  finalMaxHp: number;
  // False for every row in a PartyWiped run; reserved for future
  // continuation-phase outcomes that leave some players alive.
  survived: boolean;
  causeOfDeath: string | null;
  diedAt: string | null; // ISO 8601
  deathX: number;
  deathY: number;
}

export interface RunSummaryDto {
  outcome: RunOutcome;
  startedAt: string; // ISO 8601
  endedAt: string; // ISO 8601
  deepestFloor: number;
  enemiesKilled: number;
  players: RunSummaryPlayerDto[];
}

export interface GameStateSnapshotDto {
  sessionId: string;
  mode: GameMode;
  floorNumber: number;
  floor: FloorSnapshotDto;
  player: PlayerSnapshotDto;
  // Other players in the same session who are currently on the local
  // player's floor. Empty in solo sessions; rendering lands in Step 4.
  otherPlayers: OtherPlayerDto[];
  combat: CombatLogDto | null;
  // Step 11 — spectator state. spectatorTargetId is set when a dead player
  // is following a teammate; the snapshot's floor / position / combat are
  // then the target's. spectatableTargets lists the live + connected
  // teammates a dead player can pick from (empty for living players).
  spectatorTargetId: string | null;
  spectatableTargets: SpectatableTargetDto[];
  // Other active combats on the viewer's floor that the viewer is NOT in.
  // The renderer animates events from these so observers see teammate
  // swings; the CombatLog UI ignores them.
  ambientCombats: CombatLogDto[];
  // Step 13 — populated once the run has ended; identical content for every
  // viewer. Presence is the client's signal to render the end-of-run screen.
  runSummary: RunSummaryDto | null;
}

export const LobbyStatus = {
  Waiting: 0,
  InGame: 1,
} as const;
export type LobbyStatus = (typeof LobbyStatus)[keyof typeof LobbyStatus];

export interface LobbyMemberDto {
  playerId: string;
  username: string;
  isHost: boolean;
  joinedAt: string; // ISO 8601
}

export interface LobbyDto {
  id: string;
  code: string;
  hostPlayerId: string;
  maxPlayers: number;
  status: LobbyStatus;
  // Set once the host clicks Start; null while Waiting. Late-joiners read
  // this to know which session to connect to on /game.
  sessionId: string | null;
  members: LobbyMemberDto[];
}

export interface LobbyMembershipDto {
  localPlayerId: string;
  lobby: LobbyDto;
}
