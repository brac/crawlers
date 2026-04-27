import { AnimatedSprite, Application, Container, Sprite } from "pixi.js";
import type { EntityDto, GameStateSnapshotDto } from "../api/types";
import {
  CombatEventKind,
  EntityType,
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
  private tileLayer = new Container();
  private entityLayer = new Container();
  private playerLayer = new Container();
  private worldContainer = new Container();
  private assets: AssetLibrary;
  private renderScale = MAX_RENDER_SCALE;
  private lastSnapshot: GameStateSnapshotDto | null = null;
  private entitySprites = new Map<string, TrackedSprite>();
  private playerSprite: TrackedSprite | null = null;
  private animQueue: PendingAnim[] = [];
  private activeAnim: ActiveAnim | null = null;
  private seenEventCount = 0;
  private hadCombat = false;
  private playerWeapon: Sprite | null = null;
  private cameraShakeUntil = 0;
  private cameraShakeDuration = 0;
  private cameraShakeMagnitude = 0;

  constructor(assets: AssetLibrary) {
    this.assets = assets;
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
    this.worldContainer.addChild(this.tileLayer);
    this.worldContainer.addChild(this.entityLayer);
    this.worldContainer.addChild(this.playerLayer);
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
    this.lastSnapshot = snapshot;
    this.drawTiles(snapshot);
    this.updateEntities(snapshot, snap);
    this.updatePlayer(snapshot, snap);
    this.ingestCombatEvents(snapshot, snap);
    if (snap) this.centerCameraOnPlayer();
  }

  private drawTiles(snapshot: GameStateSnapshotDto) {
    const { width, height, tiles, visibility, rooms } = snapshot.floor;
    const salt = snapshot.floorNumber;
    this.tileLayer.removeChildren();

    // Pass 1: walls / floors / stairs. For door cells, draw a Floor base
    // so the doorway isn't a hole when the door is rendered with overflow.
    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        const i = y * width + x;
        const vis = visibility[i] as VisibilityState;
        const alpha = VISIBILITY_ALPHA[vis];
        if (alpha === 0) continue;
        const t = tiles[i] as TileType;
        const baseType = isDoorTile(t) ? TileType.Floor : t;
        this.addTileSprite(baseType, x, y, alpha, salt);
      }
    }

    // Pass 2: door overlays — drawn on top of all walls/floors so a 32×32
    // leaf sprite doesn't get partially covered by adjacent tiles iterated
    // later in pass 1.
    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        const i = y * width + x;
        const vis = visibility[i] as VisibilityState;
        const alpha = VISIBILITY_ALPHA[vis];
        if (alpha === 0) continue;
        const t = tiles[i] as TileType;
        if (!isDoorTile(t)) continue;
        this.addTileSprite(t, x, y, alpha, salt);
      }
    }

    void rooms; // room-level decorations (e.g. columns) deferred — see VISUAL_POLISH
  }

  private addTileSprite(
    t: TileType,
    x: number,
    y: number,
    alpha: number,
    salt: number,
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
    this.tileLayer.addChild(sprite);
  }


  private updateEntities(snapshot: GameStateSnapshotDto, snap: boolean) {
    const seen = new Set<string>();
    for (const e of snapshot.floor.entities) {
      seen.add(e.id);
      const target = this.entityTargetPosition(e);
      const existing = this.entitySprites.get(e.id);
      if (existing) {
        this.retarget(existing, target.x, target.y, snap);
        continue;
      }
      const created = this.spawnEntity(e);
      if (!created) continue;
      created.sprite.x = target.x;
      created.sprite.y = target.y;
      this.entityLayer.addChild(created.sprite);
      this.entitySprites.set(e.id, created);
    }
    for (const [id, tracked] of this.entitySprites.entries()) {
      if (seen.has(id)) continue;
      // Don't destroy yet if a pending or active combat anim still references
      // this entity — e.g., the killing-blow Hit/Crit needs the enemy sprite
      // around to animate against. Next snapshot picks it up after the queue
      // drains.
      if (this.isReferencedByAnim(id)) continue;
      this.entityLayer.removeChild(tracked.sprite);
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

  private updatePlayer(snapshot: GameStateSnapshotDto, snap: boolean) {
    const { x, y } = snapshot.player;
    const target = { x: (x + 0.5) * TILE_SIZE, y: (y + 1) * TILE_SIZE };
    if (!this.playerSprite) {
      const tracked = this.spawnCharacter("Player");
      if (!tracked) return;
      tracked.sprite.x = target.x;
      tracked.sprite.y = target.y;
      this.playerLayer.addChild(tracked.sprite);
      this.playerSprite = tracked;
      this.spawnPlayerWeapon();
      return;
    }
    this.retarget(this.playerSprite, target.x, target.y, snap);
  }

  private spawnPlayerWeapon() {
    const tex = this.assets.weaponTexture("regular_sword");
    if (!tex) return;
    const w = new Sprite(tex);
    // Anchor near the bottom of the sprite so the handle is the placement
    // point — the blade extends upward from the knight's hand.
    w.anchor.set(0.5, 0.95);
    // Added after the player sprite so it renders on top of him.
    this.playerLayer.addChild(w);
    this.playerWeapon = w;
  }

  // Hand sits a few px to the facing side at roughly hip height; pulled in
  // and down from the original (5, 14) so the sword no longer perches above
  // the knight's head. Easy to tweak by eye.
  private static readonly WEAPON_OFFSET_X = -5;
  private static readonly WEAPON_OFFSET_Y = 4;

  private syncPlayerWeapon() {
    if (!this.playerWeapon || !this.playerSprite || !this.lastSnapshot) return;
    const p = this.playerSprite.sprite;
    const facing = p.scale.x >= 0 ? 1 : -1;
    const now = performance.now();

    // Pick a pose: combat anim if player is involved, else gentle idle bob.
    let pose = ZERO_POSE;
    let bob = 0;
    if (this.activeAnim) {
      const pid = this.lastSnapshot.player.id;
      const isActor = this.activeAnim.actorId === pid;
      const isTarget = this.activeAnim.targetId === pid;
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

    this.playerWeapon.x =
      p.x + (DungeonRenderer.WEAPON_OFFSET_X + pose.dx) * facing;
    this.playerWeapon.y =
      p.y - (DungeonRenderer.WEAPON_OFFSET_Y + pose.dy) - bob;
    this.playerWeapon.scale.x = facing;
    // Rotation is unmirrored — Pixi's scale.x = -1 already mirrors the
    // rotation visually, so the same rotation value reads as "backward
    // relative to facing" regardless of which direction the player faces.
    this.playerWeapon.rotation = pose.rotation;
    // Mirror tint so combat hit-flashes affect the weapon too.
    this.playerWeapon.tint = p.tint;
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

  private entityTargetPosition(e: EntityDto): { x: number; y: number } {
    if (e.type === EntityType.Item) {
      return { x: (e.x + 0.5) * TILE_SIZE, y: (e.y + 0.5) * TILE_SIZE };
    }
    return { x: (e.x + 0.5) * TILE_SIZE, y: (e.y + 1) * TILE_SIZE };
  }

  private spawnEntity(e: EntityDto): TrackedSprite | null {
    if (e.type === EntityType.Item) {
      const tex = this.assets.itemTexture(e.name);
      if (!tex) return null;
      const sprite = new Sprite(tex);
      sprite.anchor.set(0.5, 0.5);
      return { sprite, tween: null, characterName: null, currentAnim: null };
    }
    return this.spawnCharacter(e.name);
  }

  private spawnCharacter(name: string): TrackedSprite | null {
    const entry = this.assets.characterEntry(name);
    const frames = entry ? this.assets.characterFrames(name, "idle") : [];
    if (!entry || frames.length === 0) return null;
    const sprite = new AnimatedSprite(frames);
    sprite.anchor.set(entry.anchor[0], entry.anchor[1]);
    sprite.animationSpeed = entry.animations.idle.fps / 60;
    sprite.loop = true;
    // Random start frame so a roomful of enemies doesn't breathe in lockstep.
    sprite.gotoAndPlay(Math.floor(Math.random() * frames.length));
    return {
      sprite,
      tween: null,
      characterName: name,
      currentAnim: "idle",
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
    for (const tracked of this.entitySprites.values()) {
      this.advanceTween(tracked, now);
    }
    if (this.playerSprite) {
      this.advanceTween(this.playerSprite, now);
    }
    this.advanceCombatAnim(now);
    this.syncPlayerWeapon();
    // Camera last so shake (Crit) is applied on top of the centered position.
    if (this.playerSprite) this.centerCameraOnPlayer();
  };

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
    const combat = snapshot.combat;
    if (!combat) {
      this.resetCombatAnim();
      this.hadCombat = false;
      return;
    }
    // Reset on snap or when combat transitions from inactive → active so
    // a fresh fight starts counting events from zero.
    if (snap || !this.hadCombat) this.resetCombatAnim();
    this.hadCombat = true;
    let n = 0;
    for (const round of combat.rounds) {
      for (const evt of round.events) {
        if (
          n >= this.seenEventCount &&
          this.shouldAnimate(evt.kind) &&
          evt.actorId &&
          evt.targetId
        ) {
          this.animQueue.push({
            kind: evt.kind,
            actorId: evt.actorId,
            targetId: evt.targetId,
          });
        }
        n++;
      }
    }
    this.seenEventCount = n;
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
    this.seenEventCount = 0;
    this.cameraShakeUntil = 0;
  }

  private findById(id: string): TrackedSprite | null {
    if (this.lastSnapshot && id === this.lastSnapshot.player.id) {
      return this.playerSprite;
    }
    return this.entitySprites.get(id) ?? null;
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
      if (!actor || !target) continue;
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
    // Reset to origin so successive frames don't accumulate.
    a.actor.sprite.x = a.actorOriginX;
    a.actor.sprite.y = a.actorOriginY;
    a.target.sprite.x = a.targetOriginX;
    a.target.sprite.y = a.targetOriginY;
    a.actor.sprite.tint = 0xffffff;
    a.target.sprite.tint = 0xffffff;

    const lunge = TILE_SIZE * LUNGE_FRACTION;
    switch (a.kind) {
      case CombatEventKind.Hit:
      case CombatEventKind.Crit: {
        a.actor.sprite.x += a.unitDx * lunge * peak;
        a.actor.sprite.y += a.unitDy * lunge * peak;
        const tint = a.kind === CombatEventKind.Crit ? CRIT_TINT : HIT_TINT;
        a.target.sprite.tint = blendColor(0xffffff, tint, peak);
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
        a.target.sprite.tint = blendColor(0xffffff, HEAL_TINT, peak);
        break;
      }
    }
  }

  private finalizeAnim(a: ActiveAnim) {
    a.actor.sprite.x = a.actorOriginX;
    a.actor.sprite.y = a.actorOriginY;
    a.target.sprite.x = a.targetOriginX;
    a.target.sprite.y = a.targetOriginY;
    a.actor.sprite.tint = 0xffffff;
    a.target.sprite.tint = 0xffffff;
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
    this.playerSprite = null;
    this.playerWeapon = null;
  }
}
