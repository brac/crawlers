import { useCallback, useEffect, useRef, useState } from "react";
import type { HubConnection } from "@microsoft/signalr";
import {
  connect,
  descend,
  flee,
  invokeUseItem,
  joinSession,
  move,
  onSnapshot,
  reviveTeammate,
  setSpectatorTarget,
} from "./api/signalr";
import type { GameStateSnapshotDto } from "./api/types";
import { EntityType, GameMode, MoveDirection, TileType } from "./api/types";
import type { AssetLibrary } from "./game/assets";
import { DungeonView } from "./game/DungeonView";
import type { CorpseTooltipInfo } from "./game/DungeonRenderer";
import { CombatLog } from "./ui/CombatLog";
import { CorpseTooltip } from "./ui/CorpseTooltip";
import { FloorAnnouncer } from "./ui/FloorAnnouncer";
import { FloorTitleCard } from "./ui/FloorTitleCard";
import { Hud } from "./ui/Hud";
import { Inventory } from "./ui/Inventory";
import { MobileControls } from "./ui/MobileControls";
import { ReviveDialog } from "./ui/ReviveDialog";
import { RunSummary } from "./ui/RunSummary";
import { SpectatorOverlay } from "./ui/SpectatorOverlay";

type Status =
  | { kind: "connecting" }
  | { kind: "joining" }
  | { kind: "ready" }
  | { kind: "error"; message: string };

function statusLabel(s: Status): string {
  switch (s.kind) {
    case "connecting":
      return "Connecting…";
    case "joining":
      return "Joining session…";
    case "ready":
      return "Connected";
    case "error":
      return `Error: ${s.message}`;
  }
}

const KEY_TO_DIRECTION: Record<string, MoveDirection> = {
  w: MoveDirection.North,
  ArrowUp: MoveDirection.North,
  s: MoveDirection.South,
  ArrowDown: MoveDirection.South,
  d: MoveDirection.East,
  ArrowRight: MoveDirection.East,
  a: MoveDirection.West,
  ArrowLeft: MoveDirection.West,
};

interface GameProps {
  assets: AssetLibrary;
  sessionId: string;
  localPlayerId: string;
  onOpenStats: () => void;
}

export function Game({ assets, sessionId, localPlayerId, onOpenStats }: GameProps) {
  const [status, setStatus] = useState<Status>({ kind: "connecting" });
  const [snapshot, setSnapshot] = useState<GameStateSnapshotDto | null>(null);
  const connectionRef = useRef<HubConnection | null>(null);
  const latestSnapRef = useRef<GameStateSnapshotDto | null>(null);
  const [corpseTip, setCorpseTip] = useState<{
    info: CorpseTooltipInfo;
    clientXY: { x: number; y: number };
  } | null>(null);

  // Stable identity so DungeonView's effect doesn't reattach listeners on
  // every snapshot update. Renderer fires this on hover / tap of a corpse;
  // null clears (pointerout on desktop, tap-elsewhere on mobile via
  // Safari's hover-on-tap simulation).
  const handleCorpseHover = useCallback(
    (info: CorpseTooltipInfo | null, clientXY: { x: number; y: number } | null) => {
      if (!info || !clientXY) {
        setCorpseTip(null);
      } else {
        setCorpseTip({ info, clientXY });
      }
    },
    [],
  );


  useEffect(() => {
    let cancelled = false;
    let unsubSnap: (() => void) | null = null;

    void (async () => {
      try {
        setStatus({ kind: "connecting" });
        const c = await connect();
        // Cancellation must be checked BEFORE assigning to the ref. In React
        // StrictMode the effect double-mounts; if mount 1's connect resolves
        // *after* mount 2's, an unguarded assignment overwrites the live
        // connection with the stale (cancelled) one — and every subsequent
        // hub invocation silently fails on a dead socket.
        if (cancelled) {
          await c.stop();
          return;
        }
        connectionRef.current = c;
        unsubSnap = onSnapshot(c, (snap) => {
          latestSnapRef.current = snap;
          setSnapshot(snap);
        });
        setStatus({ kind: "joining" });
        const initial = await joinSession(c, sessionId, localPlayerId);
        if (cancelled) {
          await c.stop();
          return;
        }
        latestSnapRef.current = initial;
        setSnapshot(initial);
        setStatus({ kind: "ready" });
      } catch (err) {
        if (cancelled) return;
        const message = err instanceof Error ? err.message : String(err);
        setStatus({ kind: "error", message });
      }
    })();

    const handleKey = (e: KeyboardEvent) => {
      const c = connectionRef.current;
      if (!c) return;
      const key = e.key.length === 1 ? e.key.toLowerCase() : e.key;

      if (key === "f") {
        e.preventDefault();
        void flee(c).catch(() => {});
        return;
      }

      if (key === ">" || key === ".") {
        e.preventDefault();
        void descend(c).catch(() => {});
        return;
      }

      // 1-9 use the corresponding consumable in inventory. In Combat the
      // server queues it for the next round; in Exploration it applies
      // immediately. Disabled in Resolution (dead).
      if (key >= "1" && key <= "9") {
        const snap = latestSnapRef.current;
        if (!snap || snap.mode === GameMode.Resolution) return;
        const idx = parseInt(key, 10) - 1;
        const consumables = snap.player.inventory.filter((i) => i.isConsumable);
        const item = consumables[idx];
        if (!item) return;
        e.preventDefault();
        void invokeUseItem(c, item.id).catch(() => {});
        return;
      }

      const dir = KEY_TO_DIRECTION[key];
      if (dir === undefined) return;
      e.preventDefault();
      void move(c, dir).catch(() => {});
    };
    window.addEventListener("keydown", handleKey);

    return () => {
      cancelled = true;
      window.removeEventListener("keydown", handleKey);
      unsubSnap?.();
      void connectionRef.current?.stop();
      connectionRef.current = null;
    };
  }, [sessionId, localPlayerId]);

  const handleSpectate = (targetId: string) => {
    const c = connectionRef.current;
    if (!c) return;
    void setSpectatorTarget(c, targetId).catch(() => {});
  };

  const handleRevive = (corpsePlayerId: string) => {
    const c = connectionRef.current;
    if (!c) return;
    void reviveTeammate(c, corpsePlayerId).catch(() => {});
  };

  // Multiplayer revive — find a reviveable teammate's corpse adjacent
  // to the local (alive) player. The dialog renders when one exists;
  // walking away naturally dismisses it (next snapshot's adjacency
  // check fails). Server enforces the same rules; this is purely a UX
  // gate for showing the button.
  const reviveTarget = (() => {
    if (!snapshot) return null;
    if (snapshot.mode !== GameMode.Exploration) return null;
    if (snapshot.player.hp <= 1) return null;

    const px = snapshot.player.x;
    const py = snapshot.player.y;
    for (const e of snapshot.floor.entities) {
      if (e.type !== EntityType.Corpse) continue;
      if (!e.playerId) continue; // unowned/legacy corpse — not reviveable
      const dx = Math.abs(e.x - px);
      const dy = Math.abs(e.y - py);
      if (Math.max(dx, dy) > 1) continue;
      // Match the corpse to a reviveable teammate by playerId — the
      // server's IsReviveable flag confirms Mode == Resolution AND
      // still connected.
      const teammate = snapshot.otherPlayers.find((op) => op.id === e.playerId);
      if (!teammate) continue;
      if (!teammate.isReviveable) continue;
      const cost = Math.max(1, Math.floor(snapshot.player.hp * 0.20));
      return { teammate, cost };
    }
    return null;
  })();

  const showCombatLog =
    snapshot?.combat &&
    (snapshot.mode === GameMode.Combat ||
      snapshot.mode === GameMode.Resolution ||
      snapshot.combat.rounds.length > 0);

  const itemsUsable = snapshot != null && snapshot.mode !== GameMode.Resolution;

  const handleMobileMove = (dir: MoveDirection) => {
    const c = connectionRef.current;
    if (!c) return;
    void move(c, dir).catch(() => {});
  };
  const handleMobileFlee = () => {
    const c = connectionRef.current;
    if (!c) return;
    void flee(c).catch(() => {});
  };
  const handleMobileDescend = () => {
    const c = connectionRef.current;
    if (!c) return;
    void descend(c).catch(() => {});
  };
  const handleUseItem = (itemId: string) => {
    const c = connectionRef.current;
    if (!c || !itemsUsable) return;
    void invokeUseItem(c, itemId).catch(() => {});
  };

  const onStairsDown = (() => {
    if (!snapshot) return false;
    const { width, tiles } = snapshot.floor;
    const idx = snapshot.player.y * width + snapshot.player.x;
    return tiles[idx] === TileType.StairsDown;
  })();

  // Run-end takes precedence over personal overlays. The dungeon stays in
  // the background as scenery; everything else (HUD, inventory, spectator
  // picker) hides because the run is over.
  const runOver = snapshot?.runSummary != null;

  return (
    <>
      {!runOver && (
        <Hud
          snapshot={snapshot}
          status={statusLabel(status)}
          onStairsDown={onStairsDown}
        />
      )}
      <DungeonView
        snapshot={snapshot}
        assets={assets}
        onCorpseHover={handleCorpseHover}
      />
      <CorpseTooltip
        info={corpseTip?.info ?? null}
        clientXY={corpseTip?.clientXY ?? null}
        floorNumber={snapshot?.floorNumber ?? 0}
      />
      {!runOver && showCombatLog && snapshot?.combat && (
        <CombatLog log={snapshot.combat} />
      )}
      {!runOver && snapshot && snapshot.mode !== GameMode.Resolution && (
        <Inventory
          items={snapshot.player.inventory}
          usable={itemsUsable}
          onUse={handleUseItem}
        />
      )}
      {!runOver && snapshot && snapshot.mode !== GameMode.Resolution && (
        <MobileControls
          onMove={handleMobileMove}
          onFlee={handleMobileFlee}
          onDescend={handleMobileDescend}
          showFlee={snapshot.mode === GameMode.Combat}
          showDescend={onStairsDown}
        />
      )}
      {!runOver && snapshot && (
        <FloorAnnouncer
          floorNumber={snapshot.floorNumber}
          flavor={snapshot.floor.flavor}
        />
      )}
      {!runOver && snapshot && (
        <FloorTitleCard
          floorNumber={snapshot.floorNumber}
          floorName={snapshot.floor.floorName}
        />
      )}
      {!runOver && (
        <SpectatorOverlay snapshot={snapshot} onSpectate={handleSpectate} />
      )}
      {!runOver && reviveTarget && snapshot && (
        <ReviveDialog
          username={reviveTarget.teammate.username}
          cost={reviveTarget.cost}
          reviverHp={snapshot.player.hp}
          onRevive={() => handleRevive(reviveTarget.teammate.id)}
        />
      )}
      {snapshot?.runSummary && (
        <RunSummary
          summary={snapshot.runSummary}
          localPlayerId={localPlayerId}
          onOpenStats={onOpenStats}
        />
      )}
    </>
  );
}
