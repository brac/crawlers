import { MoveDirection } from "../api/types";

interface MobileControlsProps {
  onMove: (dir: MoveDirection) => void;
  onFlee: () => void;
  onDescend: () => void;
  showFlee: boolean;
  showDescend: boolean;
}

export function MobileControls({
  onMove,
  onFlee,
  onDescend,
  showFlee,
  showDescend,
}: MobileControlsProps) {
  // onPointerDown beats onClick on touch — fires immediately, no 300ms tap delay
  // on browsers that still simulate it, and ignores follow-up mouse events.
  const tap = (fn: () => void) => (e: React.PointerEvent) => {
    e.preventDefault();
    fn();
  };

  return (
    <div className="mobile-controls">
      <div className="dpad" aria-label="Movement">
        <button
          className="dpad-btn dpad-up"
          onPointerDown={tap(() => onMove(MoveDirection.North))}
          aria-label="Move north"
        >
          ▲
        </button>
        <button
          className="dpad-btn dpad-left"
          onPointerDown={tap(() => onMove(MoveDirection.West))}
          aria-label="Move west"
        >
          ◀
        </button>
        <button
          className="dpad-btn dpad-right"
          onPointerDown={tap(() => onMove(MoveDirection.East))}
          aria-label="Move east"
        >
          ▶
        </button>
        <button
          className="dpad-btn dpad-down"
          onPointerDown={tap(() => onMove(MoveDirection.South))}
          aria-label="Move south"
        >
          ▼
        </button>
      </div>

      <div className="mobile-actions">
        {showDescend && (
          <button
            className="mobile-action mobile-action-descend"
            onPointerDown={tap(onDescend)}
          >
            Descend ↓
          </button>
        )}
        {showFlee && (
          <button
            className="mobile-action mobile-action-flee"
            onPointerDown={tap(onFlee)}
          >
            Flee
          </button>
        )}
      </div>
    </div>
  );
}
