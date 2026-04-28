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

  useEffect(() => {
    if (!containerRef.current) return;
    const container = containerRef.current;
    const renderer = new DungeonRenderer(assets);
    rendererRef.current = renderer;
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
    })();

    const onResize = () => {
      renderer.resize(container.clientWidth, container.clientHeight);
    };
    window.addEventListener("resize", onResize);
    // iOS Safari fires this (and not 'resize') when the address bar shows/hides.
    window.visualViewport?.addEventListener("resize", onResize);

    return () => {
      cancelled = true;
      window.removeEventListener("resize", onResize);
      window.visualViewport?.removeEventListener("resize", onResize);
      renderer.destroy();
      rendererRef.current = null;
    };
  }, [assets]);

  useEffect(() => {
    if (!snapshot || !rendererRef.current) return;
    rendererRef.current.setSnapshot(snapshot);
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
