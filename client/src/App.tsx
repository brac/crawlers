import { useEffect, useRef, useState } from "react";
import type { HubConnection } from "@microsoft/signalr";
import {
  connect,
  descend,
  flee,
  invokeUseItem,
  joinNewSession,
  move,
  onSnapshot,
} from "./api/signalr";
import type { GameStateSnapshotDto } from "./api/types";
import { GameMode, MoveDirection, TileType } from "./api/types";
import { DungeonView } from "./game/DungeonView";
import { CombatLog } from "./ui/CombatLog";
import { Hud } from "./ui/Hud";
import { Inventory } from "./ui/Inventory";
import "./App.css";

type Status =
  | { kind: "idle" }
  | { kind: "connecting" }
  | { kind: "joining" }
  | { kind: "ready" }
  | { kind: "error"; message: string };

function statusLabel(s: Status): string {
  switch (s.kind) {
    case "idle":
      return "Idle";
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

export default function App() {
  const [status, setStatus] = useState<Status>({ kind: "idle" });
  const [snapshot, setSnapshot] = useState<GameStateSnapshotDto | null>(null);
  const connectionRef = useRef<HubConnection | null>(null);
  const latestSnapRef = useRef<GameStateSnapshotDto | null>(null);

  useEffect(() => {
    let cancelled = false;
    let unsubSnap: (() => void) | null = null;

    void (async () => {
      try {
        setStatus({ kind: "connecting" });
        const c = await connect();
        connectionRef.current = c;
        if (cancelled) {
          await c.stop();
          return;
        }
        unsubSnap = onSnapshot(c, (snap) => {
          latestSnapRef.current = snap;
          setSnapshot(snap);
        });
        setStatus({ kind: "joining" });
        const initial = await joinNewSession(c);
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
  }, []);

  const handleRestart = async () => {
    const c = connectionRef.current;
    if (!c) return;
    try {
      setStatus({ kind: "joining" });
      const next = await joinNewSession(c);
      latestSnapRef.current = next;
      setSnapshot(next);
      setStatus({ kind: "ready" });
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      setStatus({ kind: "error", message });
    }
  };

  const showCombatLog =
    snapshot?.combat &&
    (snapshot.mode === GameMode.Combat ||
      snapshot.mode === GameMode.Resolution ||
      snapshot.combat.rounds.length > 0);

  const itemsUsable = snapshot != null && snapshot.mode !== GameMode.Resolution;

  const onStairsDown = (() => {
    if (!snapshot) return false;
    const { width, tiles } = snapshot.floor;
    const idx = snapshot.player.y * width + snapshot.player.x;
    return tiles[idx] === TileType.StairsDown;
  })();

  return (
    <div className="app">
      <Hud
        snapshot={snapshot}
        status={statusLabel(status)}
        onRestart={handleRestart}
        onStairsDown={onStairsDown}
      />
      <DungeonView snapshot={snapshot} />
      {showCombatLog && snapshot?.combat && <CombatLog log={snapshot.combat} />}
      {snapshot && (
        <Inventory items={snapshot.player.inventory} usable={itemsUsable} />
      )}
    </div>
  );
}
