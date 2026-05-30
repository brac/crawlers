import { useEffect, useRef } from "react";
import type { GameStateSnapshotDto } from "../api/types";
import type { AssetLibrary } from "./assets";
import { type CorpseHoverHandler, DungeonRenderer } from "./DungeonRenderer";

interface DungeonViewProps {
  snapshot: GameStateSnapshotDto | null;
  assets: AssetLibrary;
  onCorpseHover?: CorpseHoverHandler;
}

export function DungeonView({ snapshot, assets, onCorpseHover }: DungeonViewProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const rendererRef = useRef<DungeonRenderer | null>(null);
  // mount() is async; the renderer ref is set synchronously but isn't ready to
  // accept snapshots until mount resolves. Track readiness and hold the latest
  // snapshot so one that arrives mid-mount (e.g. the server pushes immediately
  // on JoinSession) is flushed once we're ready instead of silently dropped.
  const readyRef = useRef(false);
  const latestSnapshotRef = useRef<GameStateSnapshotDto | null>(null);

  useEffect(() => {
    if (!containerRef.current) return;
    const container = containerRef.current;
    const renderer = new DungeonRenderer(assets);
    rendererRef.current = renderer;
    readyRef.current = false;
    let cancelled = false;

    void (async () => {
      await renderer.mount(
        container,
        container.clientWidth,
        container.clientHeight,
      );
      if (cancelled) {
        renderer.destroy();
        return;
      }
      readyRef.current = true;
      // Flush a snapshot that landed before mount finished.
      if (latestSnapshotRef.current) {
        renderer.setSnapshot(latestSnapshotRef.current);
      }
    })();

    const onResize = () => {
      renderer.resize(container.clientWidth, container.clientHeight);
    };
    window.addEventListener("resize", onResize);
    // iOS Safari fires this (and not 'resize') when the address bar shows/hides.
    window.visualViewport?.addEventListener("resize", onResize);

    return () => {
      cancelled = true;
      readyRef.current = false;
      window.removeEventListener("resize", onResize);
      window.visualViewport?.removeEventListener("resize", onResize);
      renderer.destroy();
      rendererRef.current = null;
    };
  }, [assets]);

  useEffect(() => {
    if (!snapshot) return;
    // Always record the latest snapshot so the mount-completion handler can
    // flush it; apply immediately only once the renderer is mounted.
    latestSnapshotRef.current = snapshot;
    if (readyRef.current && rendererRef.current) {
      rendererRef.current.setSnapshot(snapshot);
    }
  }, [snapshot]);

  // Re-bind whenever the parent's handler identity changes. Cleanup nulls
  // it out so a stale handler (from a previous mount of Game.tsx) can't
  // fire into a torn-down React tree.
  useEffect(() => {
    rendererRef.current?.setOnCorpseHover(onCorpseHover ?? null);
    return () => rendererRef.current?.setOnCorpseHover(null);
  }, [onCorpseHover]);

  return <div ref={containerRef} className="dungeon-view" />;
}
