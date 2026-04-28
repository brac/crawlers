import { useEffect, useState } from "react";

interface FloorTitleCardProps {
  floorNumber: number;
  floorName: string;
}

const VISIBLE_MS = 1500; // hold time before the fade
const FADE_OUT_MS = 2000; // duration of the fade itself

type Phase = "visible" | "fading" | "hidden";

/// Step 9 — large center-screen title card shown on every floor change.
/// Reads "Floor N: NAME" in oversized white text, holds for ~1.5s,
/// fades to 0 over ~2s. Re-fires only on actual floor-number change
/// (deps include floorNumber + floorName) so steady-state snapshots
/// on the same floor don't re-trigger. State lives in async timer
/// callbacks (mirrors FloorAnnouncer's strict-mode-safe pattern).
export function FloorTitleCard({ floorNumber, floorName }: FloorTitleCardProps) {
  const [phase, setPhase] = useState<Phase>("hidden");
  const [text, setText] = useState<string | null>(null);

  useEffect(() => {
    if (!floorName) return;

    const showTimer = window.setTimeout(() => {
      setText(floorName);
      setPhase("visible");
    }, 0);
    const fadeTimer = window.setTimeout(() => setPhase("fading"), VISIBLE_MS);
    const clearTimer = window.setTimeout(() => {
      setPhase("hidden");
      setText(null);
    }, VISIBLE_MS + FADE_OUT_MS);

    return () => {
      window.clearTimeout(showTimer);
      window.clearTimeout(fadeTimer);
      window.clearTimeout(clearTimer);
    };
  }, [floorNumber, floorName]);

  if (phase === "hidden" || text === null) return null;
  return (
    <div className={`floor-title-card ${phase === "visible" ? "visible" : "fading"}`}>
      {text}
    </div>
  );
}
