import { TileType } from "../api/types";

export const TILE_SIZE = 20;

export const TILE_COLORS: Record<TileType, number> = {
  [TileType.Floor]: 0x4a3a2a,
  [TileType.Wall]: 0x222222,
  [TileType.Door]: 0x886633,
  [TileType.StairsUp]: 0x4488cc,
  [TileType.StairsDown]: 0xcc4444,
};

export const PLAYER_COLOR = 0xf2c14e;
export const ENEMY_COLOR = 0xe14b4b;
export const ITEM_COLOR = 0x6ec5b8;
export const BACKGROUND_COLOR = 0x111111;

// Per-archetype enemy appearance, keyed by EntityDto.name so adding new
// archetypes server-side just needs an entry here. Falls back to ENEMY_COLOR.
export const ENEMY_APPEARANCE: Record<
  string,
  { color: number; radiusFactor: number }
> = {
  Husk: { color: 0xe14b4b, radiusFactor: 0.4 },
  Rasper: { color: 0xff8aa6, radiusFactor: 0.32 },
  Hulk: { color: 0x8a2d2d, radiusFactor: 0.46 },
};
