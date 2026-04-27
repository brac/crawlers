// Native source-pixel size of a tile in the 0x72 atlas. The renderer picks an
// integer scale (2-3) at runtime based on viewport size — see DungeonRenderer.
export const TILE_SIZE = 16;
export const MAX_RENDER_SCALE = 3;
export const MIN_RENDER_SCALE = 2;
// Aim to show at least this many tiles along the shorter viewport dimension.
export const MIN_TILES_VISIBLE = 11;

export const BACKGROUND_COLOR = 0x111111;
