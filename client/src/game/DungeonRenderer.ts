import { Application, Container, Graphics } from "pixi.js";
import type { GameStateSnapshotDto, TileType } from "../api/types";
import { EntityType, VisibilityState } from "../api/types";
import {
  BACKGROUND_COLOR,
  ENEMY_APPEARANCE,
  ENEMY_COLOR,
  ITEM_COLOR,
  PLAYER_COLOR,
  TILE_COLORS,
  TILE_SIZE,
} from "./tileColors";

const VISIBILITY_ALPHA: Record<VisibilityState, number> = {
  [VisibilityState.Hidden]: 0,
  [VisibilityState.Explored]: 0.35,
  [VisibilityState.Visible]: 1,
};

export class DungeonRenderer {
  private app: Application | null = null;
  private tileLayer = new Graphics();
  private entityLayer = new Graphics();
  private playerLayer = new Graphics();
  private worldContainer = new Container();

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
    this.worldContainer.addChild(this.tileLayer);
    this.worldContainer.addChild(this.entityLayer);
    this.worldContainer.addChild(this.playerLayer);
    app.stage.addChild(this.worldContainer);
    parent.appendChild(app.canvas);
  }

  resize(width: number, height: number) {
    if (!this.app) return;
    this.app.renderer.resize(width, height);
  }

  setSnapshot(snapshot: GameStateSnapshotDto) {
    if (!this.app) return;
    this.drawTiles(snapshot);
    this.drawEntities(snapshot);
    this.drawPlayer(snapshot);
    this.centerOn(snapshot);
  }

  private drawEntities(snapshot: GameStateSnapshotDto) {
    const g = this.entityLayer;
    g.clear();
    const itemSize = TILE_SIZE * 0.5;
    for (const e of snapshot.floor.entities) {
      const cx = e.x * TILE_SIZE + TILE_SIZE / 2;
      const cy = e.y * TILE_SIZE + TILE_SIZE / 2;
      if (e.type === EntityType.Item) {
        // Diamond-ish square so items read differently from enemies.
        g.rect(cx - itemSize / 2, cy - itemSize / 2, itemSize, itemSize)
          .fill(ITEM_COLOR);
      } else {
        const look = ENEMY_APPEARANCE[e.name] ?? {
          color: ENEMY_COLOR,
          radiusFactor: 0.4,
        };
        g.circle(cx, cy, TILE_SIZE * look.radiusFactor).fill(look.color);
      }
    }
  }

  private drawTiles(snapshot: GameStateSnapshotDto) {
    const { width, height, tiles, visibility } = snapshot.floor;
    const g = this.tileLayer;
    g.clear();
    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        const i = y * width + x;
        const vis = visibility[i] as VisibilityState;
        const alpha = VISIBILITY_ALPHA[vis];
        if (alpha === 0) continue;
        const t = tiles[i] as TileType;
        const color = TILE_COLORS[t] ?? 0xff00ff;
        g.rect(x * TILE_SIZE, y * TILE_SIZE, TILE_SIZE, TILE_SIZE)
          .fill({ color, alpha });
      }
    }
  }

  private drawPlayer(snapshot: GameStateSnapshotDto) {
    const g = this.playerLayer;
    g.clear();
    const { x, y } = snapshot.player;
    const { width, visibility } = snapshot.floor;
    const vis = visibility[y * width + x] as VisibilityState;
    if (vis !== VisibilityState.Visible) return;
    const cx = x * TILE_SIZE + TILE_SIZE / 2;
    const cy = y * TILE_SIZE + TILE_SIZE / 2;
    const radius = TILE_SIZE * 0.4;
    g.circle(cx, cy, radius).fill(PLAYER_COLOR);
  }

  private centerOn(snapshot: GameStateSnapshotDto) {
    if (!this.app) return;
    const dpr = window.devicePixelRatio || 1;
    const viewW = this.app.renderer.width / dpr;
    const viewH = this.app.renderer.height / dpr;
    const dungeonW = snapshot.floor.width * TILE_SIZE;
    const dungeonH = snapshot.floor.height * TILE_SIZE;
    this.worldContainer.x = Math.max(0, (viewW - dungeonW) / 2);
    this.worldContainer.y = Math.max(0, (viewH - dungeonH) / 2);
  }

  destroy() {
    if (this.app) {
      this.app.destroy(true, { children: true });
      this.app = null;
    }
  }
}
