import { Assets, Rectangle, Texture } from "pixi.js";

// Manifest schema — mirrors client/public/assets/dungeon/assets.json.
// See VISUAL_POLISH.md › Manifest Schema.

export interface FrameRef {
  atlas: string;
  frame: [number, number, number, number];
  tilesWide?: number;
  tilesTall?: number;
  anchor?: [number, number];
  spriteName?: string;
  role?: string;
}

export interface AnimStrip {
  atlas: string;
  origin: [number, number];
  frameCount: number;
  frameStep: [number, number];
  fps: number;
}

export interface CharacterEntry {
  spriteName: string;
  size: [number, number];
  anchor: [number, number];
  flipX?: boolean;
  animations: {
    idle: AnimStrip;
    run: AnimStrip;
    hit?: AnimStrip;
  };
}

export interface AtlasEntry {
  src: string;
  tileSize?: number;
}

export interface AssetManifest {
  version: number;
  tilesetSource?: string;
  atlases: Record<string, AtlasEntry>;
  tiles: Record<string, FrameRef>;
  // Per-cell rotation: renderer picks one variant deterministically per cell.
  tileVariants?: Record<string, FrameRef[]>;
  // Probabilistic substitution: renderer occasionally swaps a base tile for
  // one of these (e.g. cracked / damaged variants of a wall).
  tileWeathering?: Record<string, FrameRef[]>;
  // Free-standing decoration sprites (columns, etc.) placed by the renderer
  // at specific positions chosen from the floor structure.
  decorations?: Record<string, FrameRef>;
  items: Record<string, FrameRef>;
  characters: Record<string, CharacterEntry>;
  characterExtras?: Record<string, CharacterEntry>;
  doors?: Record<string, FrameRef>;
  weapons?: Record<string, FrameRef>;
}

// Deterministic 32-bit hash of three integers — used to pick stable per-cell
// tile variants/weathering across re-renders. FNV-1a inspired.
export function hash3(x: number, y: number, z: number): number {
  let h = 2166136261;
  h = Math.imul(h ^ (x | 0), 16777619);
  h = Math.imul(h ^ (y | 0), 16777619);
  h = Math.imul(h ^ (z | 0), 16777619);
  return h >>> 0;
}

export type CharacterAnimation = "idle" | "run" | "hit";

const MANIFEST_URL = "/assets/dungeon/assets.json";

export class AssetLibrary {
  readonly manifest: AssetManifest;
  private readonly atlases: Map<string, Texture>;
  private readonly subTextures = new Map<string, Texture>();

  constructor(manifest: AssetManifest, atlases: Map<string, Texture>) {
    this.manifest = manifest;
    this.atlases = atlases;
  }

  private subTexture(ref: FrameRef): Texture {
    const [x, y, w, h] = ref.frame;
    const key = `${ref.atlas}:${x},${y},${w},${h}`;
    let t = this.subTextures.get(key);
    if (t) return t;
    const atlas = this.atlases.get(ref.atlas);
    if (!atlas) {
      throw new Error(`Unknown atlas '${ref.atlas}' for frame ${key}`);
    }
    t = new Texture({
      source: atlas.source,
      frame: new Rectangle(x, y, w, h),
    });
    this.subTextures.set(key, t);
    return t;
  }

  tileTexture(tile: string): Texture {
    const ref = this.manifest.tiles[tile];
    if (!ref) throw new Error(`No tile mapping for '${tile}'`);
    return this.subTexture(ref);
  }

  /** Resolve any FrameRef (variant, weathering, decoration) to a Texture. */
  tileTextureFromRef(ref: FrameRef): Texture {
    return this.subTexture(ref);
  }

  itemTexture(name: string): Texture | null {
    const ref = this.manifest.items[name];
    return ref ? this.subTexture(ref) : null;
  }

  weaponTexture(name: string): Texture | null {
    const ref = this.manifest.weapons?.[name];
    return ref ? this.subTexture(ref) : null;
  }

  /**
   * Returns the variant FrameRef for `name` at cell (x, y) given a salt
   * (typically the floor number), or null if no variants are defined.
   * Stable across re-renders: same cell + salt → same variant.
   */
  tileVariantFor(
    name: string,
    x: number,
    y: number,
    salt: number,
  ): FrameRef | null {
    const variants = this.manifest.tileVariants?.[name];
    if (!variants || variants.length === 0) return null;
    return variants[hash3(x, y, salt) % variants.length];
  }

  /**
   * Probabilistically returns a weathered FrameRef for `name` at cell (x, y).
   * `probability` is in [0, 1]; if the deterministic roll lands above it, or
   * no weathering exists, returns null (caller falls back to the base tile).
   */
  tileWeatheringFor(
    name: string,
    x: number,
    y: number,
    salt: number,
    probability: number,
  ): FrameRef | null {
    const variants = this.manifest.tileWeathering?.[name];
    if (!variants || variants.length === 0) return null;
    const h = hash3(x, y, salt ^ 0x9e3779b1);
    if ((h & 0xffff) / 0x10000 >= probability) return null;
    return variants[(h >>> 16) % variants.length];
  }

  decorationFrame(name: string): FrameRef | null {
    return this.manifest.decorations?.[name] ?? null;
  }

  decorationTexture(name: string): Texture | null {
    const ref = this.decorationFrame(name);
    return ref ? this.subTexture(ref) : null;
  }

  characterEntry(name: string): CharacterEntry | null {
    return (
      this.manifest.characters[name] ??
      this.manifest.characterExtras?.[name] ??
      null
    );
  }

  characterFrames(name: string, anim: CharacterAnimation): Texture[] {
    const entry = this.characterEntry(name);
    if (!entry) return [];
    const strip = entry.animations[anim];
    if (!strip) return [];
    const [w, h] = entry.size;
    const frames: Texture[] = [];
    for (let i = 0; i < strip.frameCount; i++) {
      const fx = strip.origin[0] + strip.frameStep[0] * i;
      const fy = strip.origin[1] + strip.frameStep[1] * i;
      frames.push(
        this.subTexture({ atlas: strip.atlas, frame: [fx, fy, w, h] }),
      );
    }
    return frames;
  }
}

export async function loadAssets(): Promise<AssetLibrary> {
  const res = await fetch(MANIFEST_URL);
  if (!res.ok) {
    throw new Error(
      `Failed to fetch asset manifest (${res.status} ${res.statusText})`,
    );
  }
  const manifest = (await res.json()) as AssetManifest;

  const atlases = new Map<string, Texture>();
  for (const [id, atlas] of Object.entries(manifest.atlases)) {
    const tex = await Assets.load<Texture>(atlas.src);
    // Crisp pixel-art sampling.
    tex.source.scaleMode = "nearest";
    atlases.set(id, tex);
  }

  return new AssetLibrary(manifest, atlases);
}
