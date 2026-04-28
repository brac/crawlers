import { useEffect, useRef, useState } from "react";

const SHEET_URL = "/assets/dungeon/0x72_DungeonTilesetII_v1.7/0x72_DungeonTilesetII_v1.7.png";
const TILE = 16;
const SCALE = 3;

interface HoverCell {
  x: number;
  y: number;
  width: number;
  height: number;
}

/// Dev-only sprite-coordinate probe. Hover the rendered sheet to see the
/// (x, y, width, height) FrameRef tuple for the 16×16 cell under the
/// cursor. Click+drag to extend the selection across multiple cells (for
/// declaring a 16×23 or 32×36 sprite). Click the box at the top to copy
/// the current frame tuple to your clipboard.
///
/// Activated via `?probe=sprites` on any page (App.tsx short-circuits
/// to render this instead of the normal flow). NOT shipped in production
/// builds — the probe URL just renders a different component, but in
/// principle this could be removed when no longer needed.
export function SpriteProbe() {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [cell, setCell] = useState<HoverCell | null>(null);
  const [pinned, setPinned] = useState<HoverCell | null>(null);
  const [selectStart, setSelectStart] = useState<{ x: number; y: number } | null>(null);

  function pixelToCell(clientX: number, clientY: number): { x: number; y: number } | null {
    const node = containerRef.current;
    if (!node) return null;
    const rect = node.getBoundingClientRect();
    const px = clientX - rect.left + node.scrollLeft;
    const py = clientY - rect.top + node.scrollTop;
    const sourceX = Math.floor(px / SCALE);
    const sourceY = Math.floor(py / SCALE);
    if (sourceX < 0 || sourceY < 0) return null;
    return {
      x: Math.floor(sourceX / TILE) * TILE,
      y: Math.floor(sourceY / TILE) * TILE,
    };
  }

  function handleMove(e: React.MouseEvent) {
    const c = pixelToCell(e.clientX, e.clientY);
    if (!c) return;
    if (selectStart) {
      const x0 = Math.min(selectStart.x, c.x);
      const y0 = Math.min(selectStart.y, c.y);
      const x1 = Math.max(selectStart.x, c.x) + TILE;
      const y1 = Math.max(selectStart.y, c.y) + TILE;
      setCell({ x: x0, y: y0, width: x1 - x0, height: y1 - y0 });
    } else {
      setCell({ x: c.x, y: c.y, width: TILE, height: TILE });
    }
  }

  function handleMouseDown(e: React.MouseEvent) {
    const c = pixelToCell(e.clientX, e.clientY);
    if (!c) return;
    setSelectStart({ x: c.x, y: c.y });
    setCell({ x: c.x, y: c.y, width: TILE, height: TILE });
  }

  function handleMouseUp() {
    if (cell) setPinned(cell);
    setSelectStart(null);
  }

  useEffect(() => {
    document.title = "Sprite Probe";
  }, []);

  function copyTuple(c: HoverCell) {
    const text = `[${c.x}, ${c.y}, ${c.width}, ${c.height}]`;
    navigator.clipboard?.writeText(text).catch(() => {});
  }

  return (
    <div style={{ background: "#1a1a1a", color: "#eee", minHeight: "100vh", fontFamily: "monospace", padding: "16px" }}>
      <h2 style={{ margin: "0 0 8px" }}>0x72 Dungeon Tileset II — Sprite Probe</h2>
      <div style={{ marginBottom: "12px", fontSize: "13px", lineHeight: 1.5 }}>
        Hover a cell to see its 16×16 frame tuple. Click+drag to extend across cells (for taller / wider sprites — e.g. 16×23, 32×36). Click the pinned tuple to copy it.
      </div>

      <div style={{ display: "flex", gap: "16px", marginBottom: "16px" }}>
        <div
          style={{
            background: "#2a2a2a",
            border: "1px solid #444",
            padding: "8px 12px",
            borderRadius: "4px",
            minWidth: "260px",
          }}
        >
          <div style={{ fontSize: "12px", color: "#888" }}>Hover</div>
          <div style={{ fontSize: "16px" }}>
            {cell ? `[${cell.x}, ${cell.y}, ${cell.width}, ${cell.height}]` : "—"}
          </div>
        </div>
        <button
          type="button"
          onClick={() => pinned && copyTuple(pinned)}
          disabled={!pinned}
          style={{
            background: pinned ? "#3a3a3a" : "#2a2a2a",
            color: "#eee",
            border: "1px solid #444",
            padding: "8px 12px",
            borderRadius: "4px",
            cursor: pinned ? "pointer" : "default",
            fontFamily: "inherit",
            minWidth: "260px",
            textAlign: "left",
          }}
          title={pinned ? "Click to copy" : "Click+drag the sheet to pin"}
        >
          <div style={{ fontSize: "12px", color: "#888" }}>Pinned (click to copy)</div>
          <div style={{ fontSize: "16px" }}>
            {pinned ? `[${pinned.x}, ${pinned.y}, ${pinned.width}, ${pinned.height}]` : "—"}
          </div>
        </button>
      </div>

      <div
        ref={containerRef}
        onMouseMove={handleMove}
        onMouseDown={handleMouseDown}
        onMouseUp={handleMouseUp}
        onMouseLeave={() => !selectStart && setCell(null)}
        style={{
          position: "relative",
          width: 512 * SCALE,
          height: 512 * SCALE,
          cursor: "crosshair",
          imageRendering: "pixelated",
          userSelect: "none",
        }}
      >
        <img
          src={SHEET_URL}
          alt=""
          width={512 * SCALE}
          height={512 * SCALE}
          style={{ display: "block", imageRendering: "pixelated" }}
          draggable={false}
        />
        {cell && (
          <div
            style={{
              position: "absolute",
              left: cell.x * SCALE,
              top: cell.y * SCALE,
              width: cell.width * SCALE,
              height: cell.height * SCALE,
              border: "2px solid #ffeb3b",
              boxShadow: "0 0 0 1px #000 inset",
              pointerEvents: "none",
            }}
          />
        )}
        {pinned && pinned !== cell && (
          <div
            style={{
              position: "absolute",
              left: pinned.x * SCALE,
              top: pinned.y * SCALE,
              width: pinned.width * SCALE,
              height: pinned.height * SCALE,
              border: "2px solid #4caf50",
              pointerEvents: "none",
            }}
          />
        )}
      </div>
    </div>
  );
}
