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
}

export interface RoomDto {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface FloorSnapshotDto {
  width: number;
  height: number;
  tiles: number[]; // row-major: tiles[y * width + x]
  visibility: number[]; // VisibilityState values, same row-major indexing
  rooms: RoomDto[];
  entities: EntityDto[]; // server-side fog filtering — only Visible enemies appear here
}

export interface PlayerSnapshotDto {
  id: string;
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
  outcome: CombatOutcome;
  rounds: CombatRoundDto[];
}

export interface GameStateSnapshotDto {
  sessionId: string;
  mode: GameMode;
  floorNumber: number;
  floor: FloorSnapshotDto;
  player: PlayerSnapshotDto;
  combat: CombatLogDto | null;
}
