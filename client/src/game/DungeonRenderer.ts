import {
  AnimatedSprite,
  Application,
  Container,
  type FederatedPointerEvent,
  Sprite,
  Text,
} from "pixi.js";
import type {
  CombatLogDto,
  EntityDto,
  GameStateSnapshotDto,
  TileHeatDto,
} from "../api/types";
import {
  ChestKind,
  CombatEventKind,
  EntityType,
  GameMode,
  TileType,
  VisibilityState,
} from "../api/types";
import type { AssetLibrary } from "./assets";
import {
  BACKGROUND_COLOR,
  MAX_RENDER_SCALE,
  MIN_RENDER_SCALE,
  MIN_TILES_VISIBLE,
  TILE_SIZE,
} from "./tileColors";

function pickRenderScale(viewportW: number, viewportH: number): number {
  const minDim = Math.min(viewportW, viewportH);
  const fit = Math.floor(minDim / (TILE_SIZE * MIN_TILES_VISIBLE));
  return Math.max(MIN_RENDER_SCALE, Math.min(MAX_RENDER_SCALE, fit));
}

// Parse a "#rrggbb" / "#rgb" string into a 24-bit number Pixi expects.
// Falls back to 0xffffff (identity tint) on null/undefined/malformed input
// so a malformed config never blanks out the dungeon.
// Resolves the manifest kind-prefix for a chest entity. The renderer
// uses this to fetch the 3-frame animation strip per chest kind.
function chestKindPrefix(e: { chestKind: ChestKind | null }): "Standard" | "Empty" | "Mimic" {
  return e.chestKind === ChestKind.Empty ? "Empty"
    : e.chestKind === ChestKind.Mimic ? "Mimic"
    : "Standard";
}

interface CoinFx {
  sprite: AnimatedSprite;
  groundY: number;   // y coord the coin bounces against (set to spawn tile)
  x: number;         // current x, px (world-space)
  y: number;         // current y, px (world-space)
  vx: number;        // px / s
  vy: number;        // px / s, downward positive
  bouncesLeft: number;
  // Once bouncing is exhausted (or vy collapses below threshold), start
  // fading the coin out over fadeMs and remove it.
  fadingFromMs: number | null; // performance.now() at fade start, or null
  fadeMs: number;
  lastTickMs: number; // performance.now() of the previous integration step
}

function parseHexColor(hex: string | undefined | null): number {
  if (!hex) return 0xffffff;
  const s = hex.startsWith("#") ? hex.slice(1) : hex;
  if (s.length === 3) {
    const r = s[0], g = s[1], b = s[2];
    const expanded = `${r}${r}${g}${g}${b}${b}`;
    const n = parseInt(expanded, 16);
    return Number.isNaN(n) ? 0xffffff : n;
  }
  if (s.length === 6) {
    const n = parseInt(s, 16);
    return Number.isNaN(n) ? 0xffffff : n;
  }
  return 0xffffff;
}

const VISIBILITY_ALPHA: Record<VisibilityState, number> = {
  [VisibilityState.Hidden]: 0,
  [VisibilityState.Explored]: 0.35,
  [VisibilityState.Visible]: 1,
};

const TILE_NAMES: Record<TileType, string> = {
  [TileType.Floor]: "Floor",
  [TileType.Wall]: "Wall",
  [TileType.Door]: "Door",
  [TileType.StairsUp]: "StairsUp",
  [TileType.StairsDown]: "StairsDown",
  [TileType.OpenDoor]: "OpenDoor",
  [TileType.LockedDoor]: "LockedDoor",
};

function isDoorTile(t: TileType): boolean {
  return (
    t === TileType.Door ||
    t === TileType.OpenDoor ||
    t === TileType.LockedDoor
  );
}

const TWEEN_DURATION_MS = 250;
const COMBAT_ANIM_MS = 300;
const LUNGE_FRACTION = 0.4; // fraction of a tile the actor lunges toward target
const SIDESTEP_FRACTION = 0.25; // perpendicular dodge for misses
const FUMBLE_FRACTION = 0.12;
const HIT_TINT = 0xff5555;
const CRIT_TINT = 0xff2222;
const HEAL_TINT = 0x55ff77;
const CRIT_SHAKE_PX = 6;
const CRIT_SHAKE_MS = 220;

interface Tween {
  startX: number;
  startY: number;
  targetX: number;
  targetY: number;
  startedAt: number;
  duration: number;
}

interface TrackedSprite {
  sprite: Sprite;
  tween: Tween | null;
  // null for items (no animation strips). Characters carry their archetype
  // name so we can swap idle/run on demand.
  characterName: string | null;
  currentAnim: "idle" | "run" | null;
  /** Sprite's "natural" tint when not in a combat anim. 0xffffff for local
   *  player and enemies; other players get their differentiator colour so
   *  hit flashes fade back into the right hue instead of pure white. */
  baseTint: number;
  /** Step 3.4 — entity type for the per-tick zIndex bump. Items get a
   *  +100 bump so a weapon dropped on a chest tile renders ABOVE the
   *  chest sprite (otherwise the item's center-anchored y is smaller
   *  than the chest's bottom-anchored y → item draws first → behind). */
  entityType: EntityType | null;
}

interface PendingAnim {
  kind: CombatEventKind;
  actorId: string;
  targetId: string;
}

interface ActiveAnim extends PendingAnim {
  actor: TrackedSprite;
  target: TrackedSprite;
  actorOriginX: number;
  actorOriginY: number;
  targetOriginX: number;
  targetOriginY: number;
  unitDx: number;
  unitDy: number;
  startedAt: number;
}

interface OtherPlayerEntry {
  tracked: TrackedSprite;
  label: Text;
  weapon: Sprite | null;
  /** Name of the texture currently on `weapon`. Used to detect
   *  equippedWeaponName transitions between snapshots so the texture
   *  can be swapped in place without respawning the sprite. */
  weaponName: string | null;
  /** True once this teammate's hp has hit 0. The corpse Entity at their
   *  death tile becomes the visual; we hide their character sprite + label
   *  + weapon so the corpse stands alone. */
  isDead: boolean;
}

// Tints applied to teammate sprites so the local player can tell them apart.
// Hand-picked to avoid pure white (the local player) and red/green (combat
// hit / heal tints), and to stay legible against the dungeon palette.
const OTHER_PLAYER_TINTS = [
  0x88ccff, // soft blue
  0xffcc55, // gold
  0xa6e16d, // chartreuse
  0xcc88ff, // lavender
  0xff9966, // coral
  0x66e0d6, // teal
];

const LABEL_OFFSET_PX = 4;

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

interface WeaponPose {
  dx: number; // facing-relative; +ve = forward, -ve = backward
  dy: number; // +ve = up
  rotation: number;
}

const ZERO_POSE: WeaponPose = { dx: 0, dy: 0, rotation: 0 };

// Three-phase swing: windup (back+up+tilt back) → strike (sweep forward+down)
// → recover. `big` boosts the magnitude for crits.
function attackPose(k: number, big: boolean): WeaponPose {
  const a = big ? 1.3 : 1.0;
  if (k < 0.3) {
    const t = k / 0.3;
    return { dx: -3 * t * a, dy: 5 * t * a, rotation: -0.7 * t * a };
  }
  if (k < 0.6) {
    const t = (k - 0.3) / 0.3;
    return {
      dx: lerp(-3 * a, 5 * a, t),
      dy: lerp(5 * a, -2 * a, t),
      rotation: lerp(-0.7 * a, 1.4 * a, t),
    };
  }
  const t = (k - 0.6) / 0.4;
  return {
    dx: lerp(5 * a, 0, t),
    dy: lerp(-2 * a, 0, t),
    rotation: lerp(1.4 * a, 0, t),
  };
}

// Defensive: pull sword back across the body (high tilt, raised), hold,
// return. Used when the player dodges/blocks an incoming Miss.
function blockPose(k: number): WeaponPose {
  if (k < 0.4) {
    const t = k / 0.4;
    return { dx: -4 * t, dy: 6 * t, rotation: 1.2 * t };
  }
  if (k < 0.6) return { dx: -4, dy: 6, rotation: 1.2 };
  const t = (k - 0.6) / 0.4;
  return {
    dx: lerp(-4, 0, t),
    dy: lerp(6, 0, t),
    rotation: lerp(1.2, 0, t),
  };
}

// Off-kilter shake when the player takes a hit; damps to zero by k=1.
function damagePose(k: number): WeaponPose {
  const wobble = Math.sin(Math.PI * k * 6);
  const damp = 1 - k;
  return { dx: 0, dy: 0, rotation: wobble * 0.3 * damp };
}

function fumblePose(k: number): WeaponPose {
  const wobble = Math.sin(Math.PI * k * 3);
  return { dx: -2 * wobble, dy: 0, rotation: 0.4 * wobble };
}

// Subtle 1.5 Hz idle bob — one full sin cycle per ~667 ms (matches the
// 4-frame, 6 fps player idle strip).
// Stable ±90° pick for a corpse sprite, plus a small per-corpse angle
// jitter so a row of bodies doesn't read as a uniform line. FNV-1a over
// the entity id mixes every byte (so adjacent corpses and uuid streaks
// don't collapse to the same orientation); we burn the high bit for the
// side and the next 8 bits for the jitter magnitude. Same id always
// returns the same rotation — persistent corpses keep their pose across
// reloads. Returns radians.
const CORPSE_JITTER = Math.PI / 12; // ±15°
function pickCorpseRotation(id: string): number {
  let h = 0x811c9dc5; // FNV offset basis
  for (let i = 0; i < id.length; i++) {
    h ^= id.charCodeAt(i);
    h = Math.imul(h, 0x01000193);
  }
  // High-order bits dodge any low-bit correlation that survives the mixer
  // for a small input alphabet.
  const sideBit = (h >>> 16) & 1;
  const jitter01 = ((h >>> 17) & 0xff) / 255; // 0..1
  const jitter = (jitter01 - 0.5) * 2 * CORPSE_JITTER; // ±CORPSE_JITTER
  const base = sideBit === 1 ? Math.PI / 2 : -Math.PI / 2;
  return base + jitter;
}

// Step 4 visual aging. Older corpses fade so a populated floor reads as
// "deep history with a few fresh kills" instead of a uniform sea of
// bodies. Computed once at sprite creation — corpses don't tick visibly
// during a session (the age delta over a single play is negligible).
//
// Dev-tuned tiers (compressed so the fade arc shows up within a play
// session — bump these up if you want corpses to feel more "ancient"):
//
//   < 1 min:     1.00  (fresh — full)
//   < 5 min:     0.85
//   < 15 min:    0.65
//   < 1 hour:    0.45
//   ≥ 1 hour:    0.30  (old — still legible, deeply faded)
function corpseAlpha(diedAtIso: string | null): number {
  if (!diedAtIso) return 1.0;
  const t = Date.parse(diedAtIso);
  if (Number.isNaN(t)) return 1.0;
  const ageMs = Math.max(0, Date.now() - t);
  const MIN = 60_000;
  const HOUR = 60 * MIN;
  if (ageMs < 1 * MIN) return 1.0;
  if (ageMs < 5 * MIN) return 0.85;
  if (ageMs < 15 * MIN) return 0.65;
  if (ageMs < 1 * HOUR) return 0.45;
  return 0.3;
}

// Step 5 tooltip payload — what the renderer hands to the React overlay
// when a player hovers / taps a corpse. `clientXY` is the page-absolute
// CSS-pixel position to anchor the tooltip at; null clears it.
export interface CorpseTooltipInfo {
  username: string | null;
  killerType: string | null;
  diedAtIso: string | null;
}
export type CorpseHoverHandler = (
  info: CorpseTooltipInfo | null,
  clientXY: { x: number; y: number } | null,
) => void;

// Step 9 environmental death heatmap. Each tile in the snapshot heatmap
// gets a subtle dark-blueish multiply on top of its normal floor sprite —
// "this place feels dangerous" without surfacing as a UI overlay. The
// curve normalizes against the hottest tile on the floor so a saturated
// veteran floor doesn't go entirely black, and an early floor with only
// 1-death tiles still picks up a faint ambient tint.
//
// Returns a sparse Map keyed "x,y" → tint (0xffffff = no tint).
function buildHeatTints(heatmap: TileHeatDto[]): Map<string, number> {
  const out = new Map<string, number>();
  if (heatmap.length === 0) return out;
  const maxCount = Math.max(...heatmap.map((h) => h.count));
  for (const h of heatmap) {
    // Log-ish curve: a tile with 1/Nth of the max heat still reads as
    // moderately darkened rather than negligible. Capped so even max
    // heat retains enough of the original tile texture to be readable.
    const intensity01 = Math.min(1, Math.log(1 + h.count) / Math.log(1 + maxCount));
    // Floor of 0.55 (heaviest dimming) up to 1.0 (no dimming).
    const k = 1 - intensity01 * 0.45;
    // Slight blue/purple shift on top of the dimming for a "stained" feel
    // — red and green dim a bit more than blue.
    const r = Math.round(0xff * k * 0.92);
    const g = Math.round(0xff * k * 0.92);
    const b = Math.round(0xff * k);
    out.set(`${h.x},${h.y}`, (r << 16) | (g << 8) | b);
  }
  return out;
}

// Baseline subduedness for corpses — applied at spawn regardless of age.
// Corpses should read as part of the floor rather than competing with the
// living player sprite for attention. The alpha curve in corpseAlpha()
// then dims them further over time.
const CORPSE_TINT = 0x9a8870;     // warm muted tan, multiplies the knight palette
const CORPSE_SCALE = 0.85;         // a smidge smaller than the live knight

function idleBob(now: number): number {
  const period = 667;
  const phase = (now / period) * Math.PI * 2;
  return Math.sin(phase) * 0.7;
}

function blendColor(a: number, b: number, k: number): number {
  const ar = (a >> 16) & 0xff;
  const ag = (a >> 8) & 0xff;
  const ab = a & 0xff;
  const br = (b >> 16) & 0xff;
  const bg = (b >> 8) & 0xff;
  const bb = b & 0xff;
  const r = Math.round(ar + (br - ar) * k);
  const g = Math.round(ag + (bg - ag) * k);
  const bl = Math.round(ab + (bb - ab) * k);
  return (r << 16) | (g << 8) | bl;
}

export class DungeonRenderer {
  private app: Application | null = null;
  // tileLayer: static floor + walls, drawn back-to-front by row.
  // spriteLayer: everything that can change Y or weave between rows — entities,
  //   players, weapons, *and door overlays*. Sortable so each child's zIndex
  //   (set to its anchored world Y) determines per-pixel-row draw order:
  //   a door anchored at the bottom of its tile draws *over* a player whose
  //   feet are in the row above it (correct), and *under* a player whose feet
  //   are in the row below it.
  // uiLayer: name labels — pinned above their owners and never occluded.
  private tileLayer = new Container();
  private spriteLayer = new Container();
  private uiLayer = new Container();
  private worldContainer = new Container();
  private assets: AssetLibrary;
  private renderScale = MAX_RENDER_SCALE;
  private lastSnapshot: GameStateSnapshotDto | null = null;
  private entitySprites = new Map<string, TrackedSprite>();
  private otherPlayerSprites = new Map<string, OtherPlayerEntry>();
  // Door sprites tracked by tile coord so we can swap textures (closed → open
  // → locked) without recreating the Sprite each snapshot.
  private doorSprites = new Map<string, { sprite: Sprite; type: TileType }>();
  private playerSprite: TrackedSprite | null = null;
  private animQueue: PendingAnim[] = [];
  private activeAnim: ActiveAnim | null = null;
  // Per-combat event watermark. Each entry is "I have already enqueued the
  // first N events of this combat (id)". On every snapshot we walk every
  // combat (the viewer's own + ambient teammate combats on the same floor)
  // and push the events past the watermark into the anim queue.
  private combatWatermarks = new Map<string, number>();
  // Loot that the server dropped on a kill arrives in the same snapshot as
  // the killing blow, but the dying enemy's sprite is held on-screen until
  // its Hit/Crit anim drains (see isReferencedByAnim). Spawning the item
  // immediately makes the loot "pop in" on top of an enemy that hasn't
  // visually died yet. We hold newly-appeared item ids here while any combat
  // anim is in flight and materialize them once the queue drains, so loot
  // appears only after the death blow finishes.
  private deferredItemIds = new Set<string>();
  private playerWeapon: Sprite | null = null;
  // Step 3.4 — name of the texture currently on `playerWeapon`. Used to
  // detect equippedWeaponName transitions on snapshot so the texture
  // can be swapped in place without respawning the sprite.
  private playerWeaponName: string | null = null;
  // Step 3.4 — active gold-burst coins. Each one is an AnimatedSprite
  // (continuously spinning) that bounces along a parabolic arc and
  // fades out after ~1.5s. Tick advances + culls them.
  private coinFx: CoinFx[] = [];
  private cameraShakeUntil = 0;
  private cameraShakeDuration = 0;
  private cameraShakeMagnitude = 0;
  private onCorpseHover: CorpseHoverHandler | null = null;

  constructor(assets: AssetLibrary) {
    this.assets = assets;
  }

  /// Wire a callback that receives corpse-hover events. Pass null to
  /// clear. Game.tsx sets this up so a CorpseTooltip React overlay
  /// can render at the cursor when a player hovers / taps a body.
  setOnCorpseHover(cb: CorpseHoverHandler | null) {
    this.onCorpseHover = cb;
  }

  private fireCorpseHover(info: CorpseTooltipInfo | null, event: FederatedPointerEvent | null) {
    if (!this.onCorpseHover) return;
    if (info === null || event === null || !this.app) {
      this.onCorpseHover(null, null);
      return;
    }
    // event.global is in canvas-local CSS pixels; offset by the canvas's
    // page rect to get the absolute client coords the tooltip needs.
    const rect = this.app.canvas.getBoundingClientRect();
    this.onCorpseHover(info, {
      x: rect.left + event.global.x,
      y: rect.top + event.global.y,
    });
  }

  async mount(parent: HTMLElement, width: number, height: number) {
    const app = new Application();
    await app.init({
      width,
      height,
      background: BACKGROUND_COLOR,
      antialias: false,
      autoDensity: true,
      resolution: window.devicePixelRatio || 1,
    });
    this.app = app;
    this.renderScale = pickRenderScale(width, height);
    this.worldContainer.scale.set(this.renderScale);
    this.spriteLayer.sortableChildren = true;
    this.worldContainer.addChild(this.tileLayer);
    this.worldContainer.addChild(this.spriteLayer);
    this.worldContainer.addChild(this.uiLayer);
    app.stage.addChild(this.worldContainer);
    parent.appendChild(app.canvas);
    app.ticker.add(this.tick);
  }

  resize(width: number, height: number) {
    if (!this.app) return;
    this.app.renderer.resize(width, height);
    const newScale = pickRenderScale(width, height);
    if (newScale !== this.renderScale) {
      this.renderScale = newScale;
      this.worldContainer.scale.set(this.renderScale);
    }
    this.centerCameraOnPlayer();
  }

  setSnapshot(snapshot: GameStateSnapshotDto) {
    if (!this.app) return;
    const isFirstSnapshot = this.lastSnapshot === null;
    const isNewSession =
      !isFirstSnapshot &&
      this.lastSnapshot!.sessionId !== snapshot.sessionId;
    const isNewFloor =
      !isFirstSnapshot &&
      this.lastSnapshot!.floorNumber !== snapshot.floorNumber;
    const snap = isFirstSnapshot || isNewFloor || isNewSession;
    // Step 3.4 — gold-burst FX. Detect a positive delta in the local
    // player's gold counter and pop a variable spray of spinning coins
    // at their tile. Cross-floor / new-session jumps (snap=true)
    // suppress the FX so spawning into a session doesn't fake a gold
    // burst. The snapshot ref must be captured BEFORE the lastSnapshot
    // reassign below.
    if (
      !snap &&
      this.lastSnapshot &&
      snapshot.player.gold > this.lastSnapshot.player.gold
    ) {
      const delta = snapshot.player.gold - this.lastSnapshot.player.gold;
      this.spawnCoinBurst(snapshot.player.x, snapshot.player.y, delta);
    }
    this.lastSnapshot = snapshot;
    this.applyFloorTint(snapshot.floor.tint);
    this.drawTiles(snapshot);
    this.updateDoors(snapshot, snap);
    // Ingest combat events BEFORE updating entities/players so the new round's
    // anim queue protects its referenced sprites (e.g. the enemy that just took
    // the killing blow) from being culled before tryStartNextAnim can find them.
    this.ingestCombatEvents(snapshot, snap);
    this.updateEntities(snapshot, snap);
    this.updatePlayer(snapshot, snap);
    this.updateOtherPlayers(snapshot, snap);
    // Step polish — when in combat, point the player at the enemy so
    // their swings land in the visually correct direction. Enemies don't
    // move during combat, so this is a per-snapshot idempotent orient
    // (safe to run every tick — faceHorizontally is a no-op when the
    // facing is already correct).
    this.orientForCombat(snapshot);
    if (snap) this.centerCameraOnPlayer();
  }

  /// Apply the per-floor color tint from the snapshot. Pixi v8 Container
  /// .tint multiplies child colors so #ffffff is identity; per-floor tints
  /// from floor-scaling.json bias the dungeon toward a theme (slight
  /// green for sewers, amber for caverns, red for the hellscape) without
  /// the cost of swapping the tile sprites — that's Step 8 work.
  private currentTint = 0xffffff;
  private applyFloorTint(tintHex: string | undefined | null) {
    const parsed = parseHexColor(tintHex);
    if (parsed === this.currentTint) return;
    this.currentTint = parsed;
    this.worldContainer.tint = parsed;
  }

  private drawTiles(snapshot: GameStateSnapshotDto) {
    const { width, height, tiles, visibility, rooms, heatmap } = snapshot.floor;
    const salt = snapshot.floorNumber;
    this.tileLayer.removeChildren();

    // Step 9 — build a sparse "tint per tile" map from the death heatmap.
    // Hottest tile drives the saturation curve so a floor with a few
    // 50-death tiles doesn't look entirely black, and an early floor with
    // only 1-death tiles still gets some ambient subduing.
    const tintByKey = buildHeatTints(heatmap);

    // Floors / walls / stairs only. Door overlays are drawn in updateDoors
    // (they live in spriteLayer so they can z-sort with players).
    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        const i = y * width + x;
        const vis = visibility[i] as VisibilityState;
        const alpha = VISIBILITY_ALPHA[vis];
        if (alpha === 0) continue;
        const t = tiles[i] as TileType;
        const baseType = isDoorTile(t) ? TileType.Floor : t;
        const tint = tintByKey.get(`${x},${y}`) ?? 0xffffff;
        this.addTileSprite(baseType, x, y, alpha, salt, tint);
      }
    }

    void rooms; // room-level decorations (e.g. columns) deferred — see VISUAL_POLISH
  }

  private updateDoors(snapshot: GameStateSnapshotDto, snap: boolean) {
    const { width, height, tiles, visibility } = snapshot.floor;
    const seen = new Set<string>();

    // Floor / session change: throw away every door sprite — the old map
    // doesn't apply to the new layout. Otherwise we'd leave ghosts on the
    // new floor at the old coordinates.
    if (snap) {
      for (const entry of this.doorSprites.values()) {
        this.spriteLayer.removeChild(entry.sprite);
        entry.sprite.destroy();
      }
      this.doorSprites.clear();
    }

    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        const i = y * width + x;
        const t = tiles[i] as TileType;
        if (!isDoorTile(t)) continue;
        const vis = visibility[i] as VisibilityState;
        const alpha = VISIBILITY_ALPHA[vis];
        if (alpha === 0) continue;

        const key = `${x},${y}`;
        seen.add(key);
        const existing = this.doorSprites.get(key);
        if (existing && existing.type === t) {
          existing.sprite.alpha = alpha;
          continue;
        }
        if (existing) {
          this.spriteLayer.removeChild(existing.sprite);
          existing.sprite.destroy();
          this.doorSprites.delete(key);
        }
        const sprite = this.makeDoorSprite(t, x, y, alpha);
        this.spriteLayer.addChild(sprite);
        this.doorSprites.set(key, { sprite, type: t });
      }
    }

    // Cull doors that are no longer doors (or fell out of LOS to alpha=0).
    for (const [key, entry] of this.doorSprites.entries()) {
      if (seen.has(key)) continue;
      this.spriteLayer.removeChild(entry.sprite);
      entry.sprite.destroy();
      this.doorSprites.delete(key);
    }
  }

  private makeDoorSprite(t: TileType, x: number, y: number, alpha: number): Sprite {
    const name = TILE_NAMES[t];
    const ref = this.assets.manifest.tiles[name];
    const tex = this.assets.tileTexture(name);
    const sprite = new Sprite(tex);
    const ax = ref.anchor?.[0] ?? 0.5;
    const ay = ref.anchor?.[1] ?? 1.0;
    sprite.anchor.set(ax, ay);
    sprite.x = (x + 0.5) * TILE_SIZE;
    sprite.y = (y + 1) * TILE_SIZE;
    sprite.alpha = alpha;
    // Static — anchored at the bottom of its tile, so its zIndex is the row
    // y in pixel space. Players whose feet sit in the row above (smaller y)
    // sort behind the door; players in the row below sort in front.
    sprite.zIndex = sprite.y;
    return sprite;
  }

  private addTileSprite(
    t: TileType,
    x: number,
    y: number,
    alpha: number,
    salt: number,
    tint: number,
  ) {
    const name = TILE_NAMES[t];

    // Pick a per-cell variant, weathering, or fall back to the base.
    let ref = this.assets.manifest.tiles[name];
    let texOverride: import("pixi.js").Texture | null = null;
    if (t === TileType.Floor || t === TileType.Wall) {
      // Probabilistic weathering: most cells stay on the base sprite, a
      // small fraction are swapped for a cracked/varied alternate.
      const probability = t === TileType.Wall ? 0.07 : 0.08;
      const weathered = this.assets.tileWeatheringFor(
        name,
        x,
        y,
        salt,
        probability,
      );
      if (weathered) {
        ref = weathered;
        texOverride = this.assets.tileTextureFromRef(weathered);
      }
    }
    const tex = texOverride ?? this.assets.tileTexture(name);

    const sprite = new Sprite(tex);
    const oversized =
      (ref.tilesWide ?? 1) > 1 || (ref.tilesTall ?? 1) > 1;
    if (oversized || ref.anchor) {
      // Render at native sprite size, anchored as the manifest specifies.
      // Doors use [0.5, 1.0] so the 32×32 leaf sits centered on the tile
      // and overflows upward into the doorframe row above.
      const ax = ref.anchor?.[0] ?? 0.5;
      const ay = ref.anchor?.[1] ?? 1.0;
      sprite.anchor.set(ax, ay);
      sprite.x = (x + 0.5) * TILE_SIZE;
      sprite.y = (y + 1) * TILE_SIZE;
    } else {
      sprite.x = x * TILE_SIZE;
      sprite.y = y * TILE_SIZE;
      sprite.width = TILE_SIZE;
      sprite.height = TILE_SIZE;
    }
    sprite.alpha = alpha;
    if (tint !== 0xffffff) sprite.tint = tint;
    this.tileLayer.addChild(sprite);
  }


  private updateEntities(snapshot: GameStateSnapshotDto, snap: boolean) {
    // A floor/session jump clears the combat-anim queue (ingestCombatEvents
    // ran first), so nothing is deferred — drop any stale holds from the
    // floor we just left.
    if (snap) this.deferredItemIds.clear();

    const seen = new Set<string>();
    for (const e of snapshot.floor.entities) {
      seen.add(e.id);
      const target = this.entityTargetPosition(e);
      const existing = this.entitySprites.get(e.id);
      if (existing) {
        this.retarget(existing, target.x, target.y, snap);
        // Step 3.3 — when a chest's isOpen flips true, play the open
        // animation forward once. The AnimatedSprite was created with
        // loop=false so it settles on the FullyOpen frame and stays.
        // The currentFrame check makes this idempotent: once we've
        // started or finished the play, we don't re-trigger.
        if (e.type === EntityType.Chest && e.isOpen
            && existing.sprite instanceof AnimatedSprite
            && existing.sprite.currentFrame === 0)
        {
          existing.sprite.gotoAndPlay(0);
        }
        continue;
      }
      // Hold a freshly-dropped loot item until the killing-blow anim drains
      // so it doesn't appear on top of an enemy that's still mid-death.
      // flushDeferredItems() spawns it once the queue is empty.
      if (e.type === EntityType.Item && this.combatAnimInFlight()) {
        this.deferredItemIds.add(e.id);
        continue;
      }
      this.materializeEntity(e, target.x, target.y);
    }
    for (const [id, tracked] of this.entitySprites.entries()) {
      if (seen.has(id)) continue;
      // Don't destroy yet if a pending or active combat anim still references
      // this entity — e.g., the killing-blow Hit/Crit needs the enemy sprite
      // around to animate against. Next snapshot picks it up after the queue
      // drains.
      if (this.isReferencedByAnim(id)) continue;
      this.spriteLayer.removeChild(tracked.sprite);
      tracked.sprite.destroy();
      this.entitySprites.delete(id);
    }
  }

  /// Sweep entity sprites against the latest snapshot and destroy any that
  /// dropped out of view but were kept alive while a combat anim referenced
  /// them. Safe to call every tick — when the snapshot already matches the
  /// sprite map this is a no-op.
  private cullDeferredEntities() {
    const snap = this.lastSnapshot;
    if (!snap) return;
    const alive = new Set<string>();
    for (const e of snap.floor.entities) alive.add(e.id);
    for (const [id, tracked] of this.entitySprites.entries()) {
      if (alive.has(id)) continue;
      if (this.isReferencedByAnim(id)) continue;
      this.spriteLayer.removeChild(tracked.sprite);
      tracked.sprite.destroy();
      this.entitySprites.delete(id);
    }
  }

  private isReferencedByAnim(id: string): boolean {
    if (
      this.activeAnim &&
      (this.activeAnim.actorId === id || this.activeAnim.targetId === id)
    ) {
      return true;
    }
    return this.animQueue.some(
      (p) => p.actorId === id || p.targetId === id,
    );
  }

  /// True while a combat animation is playing or queued. Used to hold
  /// loot drops off-screen until the killing blow finishes.
  private combatAnimInFlight(): boolean {
    return this.activeAnim !== null || this.animQueue.length > 0;
  }

  /// Spawn, position, and register an entity sprite. Shared by the
  /// snapshot sync and the deferred-loot flush so the two paths can't
  /// drift. No-op (returns false) when the entity has no texture.
  private materializeEntity(e: EntityDto, x: number, y: number): boolean {
    const created = this.spawnEntity(e);
    if (!created) return false;
    created.sprite.x = x;
    created.sprite.y = y;
    this.spriteLayer.addChild(created.sprite);
    this.entitySprites.set(e.id, created);
    return true;
  }

  /// Materialize loot drops that were held back while the killing-blow
  /// animation played. Called from the tick once the anim queue drains.
  /// An id that has since left the snapshot (already picked up, or we
  /// changed floors) is simply dropped.
  private flushDeferredItems() {
    if (this.deferredItemIds.size === 0) return;
    const snap = this.lastSnapshot;
    for (const id of this.deferredItemIds) {
      const e = snap?.floor.entities.find((x) => x.id === id) ?? null;
      if (e && !this.entitySprites.has(id)) {
        const target = this.entityTargetPosition(e);
        this.materializeEntity(e, target.x, target.y);
      }
    }
    this.deferredItemIds.clear();
  }

  private updateOtherPlayers(snapshot: GameStateSnapshotDto, snap: boolean) {
    const seen = new Set<string>();
    for (const op of snapshot.otherPlayers) {
      seen.add(op.id);
      const target = {
        x: (op.x + 0.5) * TILE_SIZE,
        y: (op.y + 1) * TILE_SIZE,
      };
      const existing = this.otherPlayerSprites.get(op.id);
      if (existing) {
        this.retarget(existing.tracked, target.x, target.y, snap);
        const desired = this.labelTextFor(op.username, op.inCombat);
        if (existing.label.text !== desired) existing.label.text = desired;
        existing.isDead = op.hp <= 0;
        existing.tracked.sprite.visible = !existing.isDead;
        existing.label.visible = !existing.isDead;
        if (existing.weapon) existing.weapon.visible = !existing.isDead;
        // Step 3.4 — if this teammate's equipped weapon name changed,
        // swap the texture in place. Same pattern as the local
        // player's syncPlayerWeaponTexture, just on the OtherPlayerEntry.
        this.syncOtherPlayerWeaponTexture(existing, op.equippedWeaponName);
        continue;
      }
      const tracked = this.spawnCharacter("Player");
      if (!tracked) continue;
      tracked.baseTint = this.tintForPlayer(op.id);
      tracked.sprite.tint = tracked.baseTint;
      tracked.sprite.x = target.x;
      tracked.sprite.y = target.y;
      const label = this.makeNameLabel(op.username, op.inCombat);
      label.x = target.x;
      label.y = target.y - TILE_SIZE - LABEL_OFFSET_PX;
      const initialWeaponName = op.equippedWeaponName ?? "Regular Sword";
      const weapon = this.spawnWeaponSprite(initialWeaponName);
      if (weapon) {
        weapon.tint = tracked.sprite.tint;
        // The tick loop positions it next frame — set initial pose so it
        // doesn't flicker from (0,0) on spawn.
        weapon.x = target.x + DungeonRenderer.WEAPON_OFFSET_X;
        weapon.y = target.y - DungeonRenderer.WEAPON_OFFSET_Y;
      }
      this.spriteLayer.addChild(tracked.sprite);
      if (weapon) this.spriteLayer.addChild(weapon);
      // Labels live in the always-on-top uiLayer so they're never occluded
      // by a wall, door, or another player's body.
      this.uiLayer.addChild(label);
      const entry: OtherPlayerEntry = {
        tracked,
        label,
        weapon,
        weaponName: weapon ? initialWeaponName : null,
        isDead: op.hp <= 0,
      };
      if (entry.isDead) {
        tracked.sprite.visible = false;
        label.visible = false;
        if (weapon) weapon.visible = false;
      }
      this.otherPlayerSprites.set(op.id, entry);
    }
    for (const [id, entry] of this.otherPlayerSprites.entries()) {
      if (seen.has(id)) continue;
      // Same deferral the entity cull uses: a pending or active combat anim
      // may still reference this teammate's sprite (e.g. a killing-blow Hit
      // landing as they fall off the snapshot). Destroy after the queue drains.
      if (this.isReferencedByAnim(id)) continue;
      this.spriteLayer.removeChild(entry.tracked.sprite);
      this.uiLayer.removeChild(entry.label);
      if (entry.weapon) this.spriteLayer.removeChild(entry.weapon);
      entry.tracked.sprite.destroy();
      entry.label.destroy();
      entry.weapon?.destroy();
      this.otherPlayerSprites.delete(id);
    }
  }

  private labelTextFor(username: string, inCombat: boolean): string {
    return inCombat ? `${username} ⚔` : username;
  }

  private makeNameLabel(username: string, inCombat: boolean): Text {
    const t = new Text({
      text: this.labelTextFor(username, inCombat),
      style: {
        fontFamily: "ui-monospace, Menlo, monospace",
        fontSize: 10,
        fill: 0xffffff,
        stroke: { color: 0x000000, width: 2 },
      },
    });
    t.anchor.set(0.5, 1);
    // Render at half size so the text resolves crisply when the worldContainer
    // is later scaled 2-3× by pickRenderScale; gives ~5-7 effective px in-world.
    t.scale.set(0.5);
    return t;
  }

  private tintForPlayer(id: string): number {
    // Deterministic — same player slot always picks the same colour across
    // sessions, so a teammate's tint never reshuffles mid-run.
    const cleaned = id.replace(/-/g, "").slice(0, 8);
    const n = Number.parseInt(cleaned, 16);
    const idx = Number.isFinite(n) ? n % OTHER_PLAYER_TINTS.length : 0;
    return OTHER_PLAYER_TINTS[idx];
  }

  private updatePlayer(snapshot: GameStateSnapshotDto, snap: boolean) {
    const { x, y } = snapshot.player;
    const target = { x: (x + 0.5) * TILE_SIZE, y: (y + 1) * TILE_SIZE };
    if (!this.playerSprite) {
      const tracked = this.spawnCharacter("Player");
      if (!tracked) return;
      tracked.sprite.x = target.x;
      tracked.sprite.y = target.y;
      this.spriteLayer.addChild(tracked.sprite);
      this.playerSprite = tracked;
      this.spawnPlayerWeapon(snapshot.player.equippedWeaponName ?? "Regular Sword");
      return;
    }
    this.retarget(this.playerSprite, target.x, target.y, snap);
    this.syncPlayerWeaponTexture(snapshot.player.equippedWeaponName);
  }

  private spawnWeaponSprite(name: string = "Regular Sword"): Sprite | null {
    const tex = this.assets.weaponTexture(name) ?? this.assets.weaponTexture("Regular Sword");
    if (!tex) return null;
    const w = new Sprite(tex);
    // Anchor near the bottom of the sprite so the handle is the placement
    // point — the blade extends upward from the knight's hand.
    w.anchor.set(0.5, 0.95);
    return w;
  }

  private spawnPlayerWeapon(name: string = "Regular Sword") {
    const w = this.spawnWeaponSprite(name);
    if (!w) return;
    this.spriteLayer.addChild(w);
    this.playerWeapon = w;
    this.playerWeaponName = name;
  }

  /// Step 3.4 — keep the player's held-weapon sprite in sync with the
  /// snapshot's equippedWeaponName. On change, swap the texture in
  /// place (no respawn) so the weapon-pose state and parent layer
  /// don't churn. Falls back to Regular Sword if the manifest doesn't
  /// know the new name (defensive — shouldn't happen because every
  /// weapon archetype is declared).
  private syncPlayerWeaponTexture(equippedName: string | null) {
    if (!this.playerWeapon) return;
    const desired = equippedName ?? "Regular Sword";
    if (this.playerWeaponName === desired) return;
    const tex = this.assets.weaponTexture(desired) ?? this.assets.weaponTexture("Regular Sword");
    if (!tex) return;
    this.playerWeapon.texture = tex;
    this.playerWeaponName = desired;
  }

  /// Same swap path as the local player's, but for a teammate. Called
  /// from updateOtherPlayers when an existing OtherPlayerEntry's
  /// EquippedWeaponName disagrees with the current sprite's texture.
  private syncOtherPlayerWeaponTexture(entry: OtherPlayerEntry, equippedName: string | null) {
    if (!entry.weapon) return;
    const desired = equippedName ?? "Regular Sword";
    if (entry.weaponName === desired) return;
    const tex = this.assets.weaponTexture(desired) ?? this.assets.weaponTexture("Regular Sword");
    if (!tex) return;
    entry.weapon.texture = tex;
    entry.weaponName = desired;
  }

  // Hand sits a few px to the facing side at roughly hip height; pulled in
  // and down from the original (5, 14) so the sword no longer perches above
  // the knight's head. Easy to tweak by eye.
  private static readonly WEAPON_OFFSET_X = -5;
  private static readonly WEAPON_OFFSET_Y = 4;

  private syncPlayerWeapon() {
    if (!this.playerWeapon || !this.playerSprite || !this.lastSnapshot) return;
    this.syncWeaponFor(
      this.playerSprite,
      this.playerWeapon,
      this.lastSnapshot.player.id,
    );
  }

  /// Shared by local player and teammates so the swing/block/fumble poses
  /// (windup → strike → recover) animate uniformly. Without this, the
  /// teammate body lunges from applyAnimFrame but their sword stays in
  /// idle bob, which reads as "they move but never swing" during combat.
  private syncWeaponFor(
    owner: TrackedSprite,
    weapon: Sprite,
    ownerId: string,
  ) {
    const s = owner.sprite;
    const facing = s.scale.x >= 0 ? 1 : -1;
    const now = performance.now();

    let pose = ZERO_POSE;
    let bob = 0;
    if (this.activeAnim) {
      const isActor = this.activeAnim.actorId === ownerId;
      const isTarget = this.activeAnim.targetId === ownerId;
      if (isActor || isTarget) {
        const k = Math.min(
          1,
          (now - this.activeAnim.startedAt) / COMBAT_ANIM_MS,
        );
        pose = this.weaponPoseForCombat(this.activeAnim.kind, isActor, k);
      } else {
        bob = idleBob(now);
      }
    } else {
      bob = idleBob(now);
    }

    weapon.x = s.x + (DungeonRenderer.WEAPON_OFFSET_X + pose.dx) * facing;
    weapon.y = s.y - (DungeonRenderer.WEAPON_OFFSET_Y + pose.dy) - bob;
    weapon.scale.x = facing;
    // Rotation is unmirrored — Pixi's scale.x = -1 already mirrors the
    // rotation visually, so the same rotation value reads as "backward
    // relative to facing" regardless of which direction the owner faces.
    weapon.rotation = pose.rotation;
    // Mirror tint so combat hit-flashes affect the weapon too.
    weapon.tint = s.tint;
  }

  private weaponPoseForCombat(
    kind: CombatEventKind,
    isActor: boolean,
    k: number,
  ): WeaponPose {
    if (isActor) {
      if (kind === CombatEventKind.Hit) return attackPose(k, false);
      if (kind === CombatEventKind.Crit) return attackPose(k, true);
      if (kind === CombatEventKind.Miss) return attackPose(k, false);
      if (kind === CombatEventKind.Fumble) return fumblePose(k);
      return ZERO_POSE;
    }
    // Target
    if (kind === CombatEventKind.Hit) return damagePose(k);
    if (kind === CombatEventKind.Crit) return damagePose(k);
    if (kind === CombatEventKind.Miss) return blockPose(k);
    return ZERO_POSE;
  }

  private retarget(
    t: TrackedSprite,
    targetX: number,
    targetY: number,
    snap: boolean,
  ) {
    if (snap) {
      t.sprite.x = targetX;
      t.sprite.y = targetY;
      t.tween = null;
      this.setAnim(t, "idle");
      return;
    }
    if (t.sprite.x === targetX && t.sprite.y === targetY) {
      t.tween = null;
      this.setAnim(t, "idle");
      return;
    }
    // Update facing on horizontal moves only — N/S keeps the last facing,
    // since the 0x72 tileset only ships left/right sprites (sprite-flipped).
    const dx = targetX - t.sprite.x;
    if (dx > 0) this.faceHorizontally(t, "right");
    else if (dx < 0) this.faceHorizontally(t, "left");
    t.tween = {
      startX: t.sprite.x,
      startY: t.sprite.y,
      targetX,
      targetY,
      startedAt: performance.now(),
      duration: TWEEN_DURATION_MS,
    };
    this.setAnim(t, "run");
  }

  private faceHorizontally(t: TrackedSprite, dir: "left" | "right") {
    if (!t.characterName) return;
    const entry = this.assets.characterEntry(t.characterName);
    const naturalFacesLeft = entry?.flipX ?? false;
    const wantFacesLeft = dir === "left";
    // anchor.x is 0.5 on characters, so scale.x = -1 mirrors in place.
    t.sprite.scale.x = naturalFacesLeft === wantFacesLeft ? 1 : -1;
  }

  /// While in combat, orient the player toward the enemy so the
  /// swing/lunge animation points the right way and the held-weapon
  /// sprite mirrors with the body. Picks the closest alive enemy on
  /// the snapshot's floor (engagement guarantees one is in range).
  /// dx === 0 keeps the current facing — same-column foes don't have a
  /// horizontal "side" so flipping is meaningless.
  private orientForCombat(snapshot: GameStateSnapshotDto) {
    if (!this.playerSprite) return;
    if (snapshot.mode !== GameMode.Combat) return;

    const px = snapshot.player.x;
    const py = snapshot.player.y;
    let target: { x: number; y: number } | null = null;
    let bestDist = Infinity;
    for (const e of snapshot.floor.entities) {
      if (e.type !== EntityType.Enemy) continue;
      const dx = Math.abs(e.x - px);
      const dy = Math.abs(e.y - py);
      const cheb = Math.max(dx, dy);
      if (cheb < bestDist) {
        bestDist = cheb;
        target = { x: e.x, y: e.y };
      }
    }
    if (!target) return;

    const dx = target.x - px;
    if (dx > 0) this.faceHorizontally(this.playerSprite, "right");
    else if (dx < 0) this.faceHorizontally(this.playerSprite, "left");
  }

  private entityTargetPosition(e: EntityDto): { x: number; y: number } {
    if (e.type === EntityType.Item) {
      return { x: (e.x + 0.5) * TILE_SIZE, y: (e.y + 0.5) * TILE_SIZE };
    }
    return { x: (e.x + 0.5) * TILE_SIZE, y: (e.y + 1) * TILE_SIZE };
  }

  private spawnEntity(e: EntityDto): TrackedSprite | null {
    if (e.type === EntityType.Item) {
      // Step 3.4 — items can be either consumables (manifest > items) or
      // weapons (manifest > weapons). Try the items section first; fall
      // through to weapons so weapon drops on the floor render correctly.
      const tex = this.assets.itemTexture(e.name) ?? this.assets.weaponTexture(e.name);
      if (!tex) return null;
      const sprite = new Sprite(tex);
      sprite.anchor.set(0.5, 0.5);
      return { sprite, tween: null, characterName: null, currentAnim: null, baseTint: 0xffffff, entityType: EntityType.Item };
    }
    if (e.type === EntityType.Chest) {
      // Step 3.3 — chests use the 3-frame animation strip
      // (Closed → Opening → FullyOpen) per kind. Initial frame depends
      // on whether the snapshot says the chest is already open: a
      // freshly-placed closed chest sits on frame 0, a chest the player
      // hasn't seen yet that's already open jumps to frame 2 (no
      // animation). The closed→open transition (handled in
      // updateEntities) calls gotoAndPlay to play the strip once and
      // settle on frame 2.
      const frames = this.assets.chestAnimationFrames(chestKindPrefix(e));
      if (frames.length === 0) return null;
      const sprite = new AnimatedSprite(frames);
      sprite.anchor.set(0.5, 1.0);
      sprite.loop = false;
      sprite.animationSpeed = 8 / 60; // ~8 fps → 3 frames in ~375ms
      sprite.gotoAndStop(e.isOpen ? frames.length - 1 : 0);
      return { sprite, tween: null, characterName: null, currentAnim: null, baseTint: 0xffffff, entityType: EntityType.Chest };
    }
    if (e.type === EntityType.Corpse) {
      // Reuse the manifest's "Corpse" item texture (knight idle frame 0,
      // 16×28). Rotated ±90° so the knight lies on his side; the side is
      // picked from a hash over the full entity id so the same corpse keeps
      // the same orientation across reloads (persistent rows hydrate with
      // a stable id), while the field of corpses mixes left- and right-
      // facing bodies. Mixing the whole id avoids the streaks you get from
      // a single-byte parity check (uuids share a small alphabet of leading
      // hex chars and you can easily hit 6+ in a row of one parity).
      const tex = this.assets.itemTexture("Corpse");
      if (!tex) return null;
      const sprite = new Sprite(tex);
      sprite.anchor.set(0.5, 0.5);
      sprite.rotation = pickCorpseRotation(e.id);
      sprite.alpha = corpseAlpha(e.diedAt);
      sprite.tint = CORPSE_TINT;
      sprite.scale.set(CORPSE_SCALE);

      // Step 5: hover / tap surfaces a tooltip with the dead player's
      // username, killer, and time-since-death. Mobile Safari fires
      // pointerover on first tap and pointerout on the next tap (or scroll),
      // so the same handlers cover desktop hover and mobile tap-to-toggle
      // without special casing.
      const tip: CorpseTooltipInfo = {
        username: e.username,
        killerType: e.killerType,
        diedAtIso: e.diedAt,
      };
      sprite.eventMode = "static";
      sprite.cursor = "pointer";
      sprite.on("pointerover", (ev: FederatedPointerEvent) => this.fireCorpseHover(tip, ev));
      sprite.on("pointermove", (ev: FederatedPointerEvent) => this.fireCorpseHover(tip, ev));
      sprite.on("pointerout", () => this.fireCorpseHover(null, null));
      sprite.on("pointertap", (ev: FederatedPointerEvent) => this.fireCorpseHover(tip, ev));

      return { sprite, tween: null, characterName: null, currentAnim: null, baseTint: CORPSE_TINT, entityType: EntityType.Corpse };
    }
    const tracked = this.spawnCharacter(e.name);
    if (tracked) tracked.entityType = e.type;
    return tracked;
  }

  private spawnCharacter(name: string): TrackedSprite | null {
    const entry = this.assets.characterEntry(name);
    const frames = entry ? this.assets.characterFrames(name, "idle") : [];
    if (!entry || frames.length === 0) return null;
    const sprite = new AnimatedSprite(frames);
    sprite.anchor.set(entry.anchor[0], entry.anchor[1]);
    // Optional per-character render-scale multiplier (manifest field).
    // Anchored at bottom-center, so larger sprites still plant their
    // feet on the same tile and just grow upward / outward.
    if (entry.scale && entry.scale !== 1) sprite.scale.set(entry.scale, entry.scale);
    sprite.animationSpeed = entry.animations.idle.fps / 60;
    sprite.loop = true;
    // Random start frame so a roomful of enemies doesn't breathe in lockstep.
    sprite.gotoAndPlay(Math.floor(Math.random() * frames.length));
    return {
      sprite,
      tween: null,
      characterName: name,
      currentAnim: "idle",
      baseTint: 0xffffff,
      entityType: null,
    };
  }

  private setAnim(t: TrackedSprite, anim: "idle" | "run") {
    if (!t.characterName || t.currentAnim === anim) return;
    const frames = this.assets.characterFrames(t.characterName, anim);
    if (frames.length === 0) return;
    const a = t.sprite as AnimatedSprite;
    const entry = this.assets.characterEntry(t.characterName);
    const strip = entry?.animations[anim];
    a.textures = frames;
    if (strip) a.animationSpeed = strip.fps / 60;
    a.loop = true;
    a.gotoAndPlay(0);
    t.currentAnim = anim;
  }

  private tick = () => {
    const now = performance.now();

    // 1) Advance positions: tile tweens first, then any combat-anim lunge or
    //    sidestep on top. Doing combat anim *before* the derived-state writes
    //    below means labels and weapons follow the lunge in real time rather
    //    than lagging by one frame.
    for (const tracked of this.entitySprites.values()) this.advanceTween(tracked, now);
    if (this.playerSprite) this.advanceTween(this.playerSprite, now);
    for (const op of this.otherPlayerSprites.values()) this.advanceTween(op.tracked, now);
    this.advanceCombatAnim(now);
    this.advanceCoinFx(now);
    // After the anim queue drains, drop any entity sprite that's no longer in
    // the latest snapshot. Snapshot-time culling defers when the queue still
    // references a sprite (so the killing-blow anim has something to swing
    // at), and without this pass a dead enemy / dropped item would linger
    // until the next *unrelated* server broadcast.
    if (!this.activeAnim && this.animQueue.length === 0) {
      this.cullDeferredEntities();
      // The killing blow has finished — drop any loot we held back so it
      // appears now rather than on top of the still-dying enemy.
      this.flushDeferredItems();
    }

    // 2) Derived state: zIndex from anchored Y, labels above their owner,
    //    teammate weapons positioned at the body. Local-player weapon goes
    //    through syncPlayerWeapon which also handles combat-driven poses.
    for (const tracked of this.entitySprites.values()) {
      // Items get a +100 z-bump so a weapon dropped on a chest tile
      // renders ABOVE the chest sprite (item's centered anchor gives a
      // smaller world-y than the chest's bottom anchor at the same
      // tile, which would otherwise put the item BEHIND the chest).
      const bump = tracked.entityType === EntityType.Item ? 100 : 0;
      tracked.sprite.zIndex = tracked.sprite.y + bump;
    }
    if (this.playerSprite) {
      this.playerSprite.sprite.zIndex = this.playerSprite.sprite.y;
    }
    for (const [id, op] of this.otherPlayerSprites.entries()) {
      op.tracked.sprite.zIndex = op.tracked.sprite.y;
      op.label.x = op.tracked.sprite.x;
      op.label.y = op.tracked.sprite.y - TILE_SIZE - LABEL_OFFSET_PX;
      if (op.weapon) {
        // Combat-aware pose, same path the local player uses — so a teammate
        // who's the actor of an active anim swings their sword instead of
        // riding the body's lunge with a static idle bob.
        this.syncWeaponFor(op.tracked, op.weapon, id);
        op.weapon.zIndex = op.tracked.sprite.y;
        const opSnap = this.findOtherPlayer(op.tracked);
        op.weapon.visible =
          !op.isDead && !this.isBehindDoor(opSnap?.x, opSnap?.y);
      }
    }
    this.syncPlayerWeapon();
    const snap = this.lastSnapshot;
    const localDead = (snap?.player.hp ?? 1) <= 0;
    if (this.playerSprite) {
      // Mirror the teammate-hide-when-dead rule for the local player so the
      // corpse skull at the death tile is the only thing visible there. The
      // death banner / mode=Resolution still drives the HUD overlay.
      this.playerSprite.sprite.visible = !localDead;
    }
    if (this.playerWeapon && this.playerSprite) {
      this.playerWeapon.zIndex = this.playerSprite.sprite.y;
      this.playerWeapon.visible =
        !localDead && !this.isBehindDoor(snap?.player.x, snap?.player.y);
    }

    // 3) Camera last so shake (Crit) is applied on top of the centered position.
    if (this.playerSprite) this.centerCameraOnPlayer();
  };

  /// Returns true when the tile directly south of (x, y) is a door — i.e. the
  /// player at (x, y) is standing one row north of a door whose 32-px-tall
  /// sprite extends up into their row. Used to hide weapons whose blade would
  /// otherwise poke above the door's top edge.
  private isBehindDoor(x: number | undefined, y: number | undefined): boolean {
    if (x === undefined || y === undefined) return false;
    const snap = this.lastSnapshot;
    if (!snap) return false;
    const { width, height, tiles } = snap.floor;
    const sy = y + 1;
    if (sy < 0 || sy >= height) return false;
    if (x < 0 || x >= width) return false;
    const t = tiles[sy * width + x] as TileType;
    return (
      t === TileType.Door ||
      t === TileType.OpenDoor ||
      t === TileType.LockedDoor
    );
  }

  private findOtherPlayer(tracked: TrackedSprite) {
    const snap = this.lastSnapshot;
    if (!snap) return undefined;
    for (const [id, entry] of this.otherPlayerSprites.entries()) {
      if (entry.tracked === tracked) {
        return snap.otherPlayers.find((p) => p.id === id);
      }
    }
    return undefined;
  }

  private advanceTween(t: TrackedSprite, now: number) {
    const tw = t.tween;
    if (!tw) return;
    const k = Math.min(1, (now - tw.startedAt) / tw.duration);
    // Ease-out quadratic: fast start, gentle landing.
    const e = 1 - (1 - k) * (1 - k);
    t.sprite.x = tw.startX + (tw.targetX - tw.startX) * e;
    t.sprite.y = tw.startY + (tw.targetY - tw.startY) * e;
    if (k >= 1) {
      t.tween = null;
      this.setAnim(t, "idle");
    }
  }

  /// Step 3.4 — pop a variable spray of spinning coins out of the given
  /// tile. Coin count is driven by the gold delta (small chest yields
  /// fling 3-ish coins, big yields scatter 10+) with a random divisor
  /// so the same delta sometimes feels like a handful and sometimes
  /// like a treasure pile. Each coin gets randomized initial velocity;
  /// gravity pulls it down; on ground impact it bounces with energy
  /// loss (restitution ~0.55). After ~3 bounces vertical motion damps
  /// out and the sprite fades to 0 over ~350ms.
  private spawnCoinBurst(tileX: number, tileY: number, goldAmount: number) {
    const frames = this.assets.effectFrames("Coin");
    if (frames.length === 0) return; // manifest missing — silent skip

    const baseX = (tileX + 0.5) * TILE_SIZE;
    const baseY = (tileY + 0.5) * TILE_SIZE;

    // Count: scaled by amount with a 1-3 random divisor for jitter.
    //   big yield (delta=20), divisor=1 → ~21 → clamped to 12 (treasure pile)
    //   big yield, divisor=3            → ~7 (modest handful)
    //   small yield (delta=5), divisor=1 → ~6 (small fistful)
    //   small yield, divisor=3           → ~2 (just a couple)
    const divisor = 1 + Math.floor(Math.random() * 3);
    const raw = 1 + Math.floor(goldAmount / divisor);
    const count = Math.max(2, Math.min(12, raw));
    const now = performance.now();

    for (let i = 0; i < count; i++) {
      const sprite = new AnimatedSprite(frames);
      sprite.anchor.set(0.5, 0.5);
      sprite.loop = true;
      sprite.animationSpeed = 8 / 60; // 2-frame strip looks fast at 8 fps
      sprite.gotoAndPlay(Math.floor(Math.random() * frames.length));
      sprite.x = baseX;
      sprite.y = baseY;
      sprite.zIndex = baseY + 1000;
      this.spriteLayer.addChild(sprite);

      // Initial cone: horizontal jitter [-30, 30] px/s, upward [-90, -130]
      // px/s. Tuned so the first arc rises ~10–14 px above the tile and
      // first impact lands ~250–350ms after spawn — chunky enough for
      // the bounce to read.
      const vx = (Math.random() - 0.5) * 60;
      const vy = -(90 + Math.random() * 40);

      this.coinFx.push({
        sprite,
        groundY: baseY,
        x: baseX,
        y: baseY,
        vx,
        vy,
        bouncesLeft: 3,
        fadingFromMs: null,
        fadeMs: 350,
        lastTickMs: now,
      });
    }
  }

  /// Per-tick discrete-time integration of active coin sprites:
  ///   - vy += gravity * dt
  ///   - position += velocity * dt
  ///   - if y crosses groundY: snap, invert vy with restitution loss,
  ///     dampen vx, decrement bouncesLeft. When bouncesLeft hits 0 (or
  ///     vy collapses below a small threshold), start fading.
  private advanceCoinFx(now: number) {
    if (this.coinFx.length === 0) return;
    const gravity = 480;        // px / s²
    const restitution = 0.55;   // y velocity preserved per bounce
    const friction = 0.78;      // x velocity preserved per bounce
    const minBounceVy = 35;     // below this, settle and fade

    const surviving: CoinFx[] = [];
    for (const c of this.coinFx) {
      const dt = Math.min(0.05, (now - c.lastTickMs) / 1000); // clamp big gaps
      c.lastTickMs = now;

      // Integrate.
      c.vy += gravity * dt;
      c.x += c.vx * dt;
      c.y += c.vy * dt;

      // Ground impact.
      if (c.y >= c.groundY && c.vy > 0) {
        c.y = c.groundY;
        if (c.bouncesLeft > 0 && c.vy > minBounceVy) {
          c.vy = -c.vy * restitution;
          c.vx *= friction;
          c.bouncesLeft--;
        } else {
          c.vy = 0;
          c.vx = 0;
          if (c.fadingFromMs === null) c.fadingFromMs = now;
        }
      }

      // Apply position + zIndex.
      c.sprite.x = c.x;
      c.sprite.y = c.y;
      c.sprite.zIndex = c.y + 1000;

      // Fade-out + cull.
      if (c.fadingFromMs !== null) {
        const fadeT = (now - c.fadingFromMs) / c.fadeMs;
        if (fadeT >= 1) {
          this.spriteLayer.removeChild(c.sprite);
          c.sprite.destroy();
          continue;
        }
        c.sprite.alpha = 1 - fadeT;
      }

      surviving.push(c);
    }
    this.coinFx = surviving;
  }

  private centerCameraOnPlayer() {
    if (!this.app || !this.playerSprite) return;
    const viewW = this.app.screen.width;
    const viewH = window.visualViewport?.height ?? this.app.screen.height;
    // Sprite is anchored at feet (y = tile bottom). Focus camera on tile
    // center so the player sits roughly at viewport center, not their feet.
    const focusX = this.playerSprite.sprite.x;
    const focusY = this.playerSprite.sprite.y - TILE_SIZE * 0.5;
    let cx = viewW / 2 - focusX * this.renderScale;
    let cy = viewH / 2 - focusY * this.renderScale;
    const now = performance.now();
    if (now < this.cameraShakeUntil && this.cameraShakeDuration > 0) {
      const remaining =
        (this.cameraShakeUntil - now) / this.cameraShakeDuration;
      const m = this.cameraShakeMagnitude * remaining;
      cx += (Math.random() - 0.5) * 2 * m;
      cy += (Math.random() - 0.5) * 2 * m;
    }
    this.worldContainer.x = cx;
    this.worldContainer.y = cy;
  }

  private ingestCombatEvents(
    snapshot: GameStateSnapshotDto,
    snap: boolean,
  ) {
    // Floor / session change: throw away every watermark and any in-flight
    // anim. Combats that lived on the old floor are gone; new ones start
    // counting from zero.
    if (snap) {
      this.resetCombatAnim();
      this.combatWatermarks.clear();
    }

    // Collect the combats whose events should drive renderer anims. The
    // viewer's own combat (when they're a participant) and every ambient
    // combat on their floor share the same animation pipeline; the only
    // thing the CombatLog UI consumes separately is snapshot.combat.
    const sources: CombatLogDto[] = [];
    if (snapshot.combat) sources.push(snapshot.combat);
    for (const c of snapshot.ambientCombats) sources.push(c);

    // TEMP DEBUG — verify ambient combat path is delivering events.
    if (snapshot.ambientCombats.length > 0 || snapshot.combat) {
      const summary = sources.map((c) => ({
        id: c.id?.slice(0, 8) ?? "(no id)",
        rounds: c.rounds.length,
        events: c.rounds.reduce((n, r) => n + r.events.length, 0),
        animatableEvents: c.rounds.reduce(
          (n, r) =>
            n +
            r.events.filter(
              (e) => this.shouldAnimate(e.kind) && e.actorId && e.targetId,
            ).length,
          0,
        ),
      }));
      console.debug(
        "[combat-anim] sources=",
        sources.length,
        "ambient=",
        snapshot.ambientCombats.length,
        "watermarks=",
        Object.fromEntries(this.combatWatermarks),
        "detail=",
        summary,
      );
    }

    const seen = new Set<string>();
    for (const combat of sources) {
      seen.add(combat.id);
      const watermark = this.combatWatermarks.get(combat.id) ?? 0;
      let n = 0;
      let pushed = 0;
      for (const round of combat.rounds) {
        for (const evt of round.events) {
          if (
            n >= watermark &&
            this.shouldAnimate(evt.kind) &&
            evt.actorId &&
            evt.targetId
          ) {
            this.animQueue.push({
              kind: evt.kind,
              actorId: evt.actorId,
              targetId: evt.targetId,
            });
            pushed++;
          }
          n++;
        }
      }
      if (pushed > 0) {
        console.debug(
          "[combat-anim] pushed",
          pushed,
          "events for combat",
          combat.id.slice(0, 8),
          "(watermark",
          watermark,
          "→",
          n,
          ")",
        );
      }
      this.combatWatermarks.set(combat.id, n);
    }

    // Drop watermarks for combats that no longer appear in the snapshot —
    // they've ended (or moved off-floor) and shouldn't keep stale entries
    // around forever.
    for (const id of [...this.combatWatermarks.keys()]) {
      if (!seen.has(id)) this.combatWatermarks.delete(id);
    }
  }

  private shouldAnimate(kind: CombatEventKind): boolean {
    return (
      kind === CombatEventKind.Hit ||
      kind === CombatEventKind.Crit ||
      kind === CombatEventKind.Miss ||
      kind === CombatEventKind.Fumble ||
      kind === CombatEventKind.Heal
    );
  }

  private resetCombatAnim() {
    if (this.activeAnim) this.finalizeAnim(this.activeAnim);
    this.activeAnim = null;
    this.animQueue = [];
    this.cameraShakeUntil = 0;
  }

  private findById(id: string): TrackedSprite | null {
    if (this.lastSnapshot && id === this.lastSnapshot.player.id) {
      return this.playerSprite;
    }
    const ent = this.entitySprites.get(id);
    if (ent) return ent;
    const op = this.otherPlayerSprites.get(id);
    if (op) return op.tracked;
    return null;
  }

  private advanceCombatAnim(now: number) {
    if (!this.activeAnim) {
      this.tryStartNextAnim(now);
      if (!this.activeAnim) return;
    }
    const a = this.activeAnim;
    const k = Math.min(1, (now - a.startedAt) / COMBAT_ANIM_MS);
    this.applyAnimFrame(a, k);
    if (k >= 1) {
      this.finalizeAnim(a);
      this.activeAnim = null;
    }
  }

  private tryStartNextAnim(now: number) {
    while (this.animQueue.length > 0) {
      const pending = this.animQueue.shift()!;
      const actor = this.findById(pending.actorId);
      const target = this.findById(pending.targetId);
      if (!actor || !target) {
        console.debug(
          "[combat-anim] skip event — missing sprite. kind=",
          pending.kind,
          "actor=",
          pending.actorId.slice(0, 8),
          actor ? "OK" : "MISSING",
          "target=",
          pending.targetId.slice(0, 8),
          target ? "OK" : "MISSING",
        );
        continue;
      }
      console.debug(
        "[combat-anim] start kind=",
        pending.kind,
        "actor=",
        pending.actorId.slice(0, 8),
        "target=",
        pending.targetId.slice(0, 8),
      );
      const dx = target.sprite.x - actor.sprite.x;
      const dy = target.sprite.y - actor.sprite.y;
      const len = Math.hypot(dx, dy);
      const unitDx = len > 0 ? dx / len : 1;
      const unitDy = len > 0 ? dy / len : 0;
      this.activeAnim = {
        ...pending,
        actor,
        target,
        actorOriginX: actor.sprite.x,
        actorOriginY: actor.sprite.y,
        targetOriginX: target.sprite.x,
        targetOriginY: target.sprite.y,
        unitDx,
        unitDy,
        startedAt: now,
      };
      if (pending.kind === CombatEventKind.Crit) {
        this.startCameraShake(CRIT_SHAKE_PX, CRIT_SHAKE_MS);
      }
      return;
    }
  }

  private applyAnimFrame(a: ActiveAnim, k: number) {
    // sin(πk): 0 → 1 (peak at k=0.5) → 0. Smooth out-and-back curve.
    const peak = Math.sin(Math.PI * k);
    // Reset to origin + base tint each frame so successive frames don't
    // accumulate, and a tinted teammate fades back through their natural
    // colour rather than washing through pure white.
    a.actor.sprite.x = a.actorOriginX;
    a.actor.sprite.y = a.actorOriginY;
    a.target.sprite.x = a.targetOriginX;
    a.target.sprite.y = a.targetOriginY;
    a.actor.sprite.tint = a.actor.baseTint;
    a.target.sprite.tint = a.target.baseTint;

    const lunge = TILE_SIZE * LUNGE_FRACTION;
    switch (a.kind) {
      case CombatEventKind.Hit:
      case CombatEventKind.Crit: {
        a.actor.sprite.x += a.unitDx * lunge * peak;
        a.actor.sprite.y += a.unitDy * lunge * peak;
        const tint = a.kind === CombatEventKind.Crit ? CRIT_TINT : HIT_TINT;
        a.target.sprite.tint = blendColor(a.target.baseTint, tint, peak);
        break;
      }
      case CombatEventKind.Miss: {
        a.actor.sprite.x += a.unitDx * lunge * peak;
        a.actor.sprite.y += a.unitDy * lunge * peak;
        // Sidestep perpendicular to the line of attack.
        const px = -a.unitDy;
        const py = a.unitDx;
        const sidestep = TILE_SIZE * SIDESTEP_FRACTION;
        a.target.sprite.x += px * sidestep * peak;
        a.target.sprite.y += py * sidestep * peak;
        break;
      }
      case CombatEventKind.Fumble: {
        const jitter =
          Math.sin(Math.PI * k * 4) * (TILE_SIZE * FUMBLE_FRACTION);
        a.actor.sprite.x += jitter;
        break;
      }
      case CombatEventKind.Heal: {
        a.target.sprite.tint = blendColor(a.target.baseTint, HEAL_TINT, peak);
        break;
      }
    }
  }

  private finalizeAnim(a: ActiveAnim) {
    a.actor.sprite.x = a.actorOriginX;
    a.actor.sprite.y = a.actorOriginY;
    a.target.sprite.x = a.targetOriginX;
    a.target.sprite.y = a.targetOriginY;
    a.actor.sprite.tint = a.actor.baseTint;
    a.target.sprite.tint = a.target.baseTint;
  }

  private startCameraShake(magnitude: number, durationMs: number) {
    this.cameraShakeUntil = performance.now() + durationMs;
    this.cameraShakeDuration = durationMs;
    this.cameraShakeMagnitude = magnitude;
  }

  destroy() {
    if (this.app) {
      this.app.ticker.remove(this.tick);
      this.app.destroy(true, { children: true });
      this.app = null;
    }
    this.entitySprites.clear();
    this.otherPlayerSprites.clear();
    this.doorSprites.clear();
    this.playerSprite = null;
    this.playerWeapon = null;
  }
}
