import { useEffect, useRef } from "react";
import type { GameStateSnapshotDto } from "../api/types";
import type { AssetLibrary } from "./assets";
import { DungeonRenderer } from "./DungeonRenderer";

interface DungeonViewProps {
  snapshot: GameStateSnapshotDto | null;
  assets: AssetLibrary;
}

export function DungeonView({ snapshot, assets }: DungeonViewProps) {
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

  return <div ref={containerRef} className="dungeon-view" />;
}
