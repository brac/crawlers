import { useEffect, useState } from "react";

interface FloorAnnouncerProps {
  floorNumber: number;
  flavor: string | null;
}

const VISIBLE_MS = 10_000; // hold time before fade-out
const FADE_OUT_MS = 600; // matches the CSS transition

type Phase = "visible" | "fading" | "hidden";

/// Step 12 — bleak announcer line shown briefly at the bottom of the
/// screen on every floor change. Re-fires only when (floorNumber, flavor)
/// changes, so steady-state snapshots on the same floor don't re-trigger.
///
/// All setState lives inside async timer callbacks. The previous
/// implementation set state synchronously in the effect body and tracked
/// "last shown floor" via a ref, which broke under React 19 strict mode:
/// the effect's cleanup cancelled the in-flight fade timers, and the
/// re-fire early-returned because the ref already matched, leaving the
/// banner stuck visible. This version trusts the dependency array to
/// dedup re-renders and lets cleanup-then-re-fire schedule fresh timers,
/// so the banner always clears on schedule.
export function FloorAnnouncer({ floorNumber, flavor }: FloorAnnouncerProps) {
  const [phase, setPhase] = useState<Phase>("hidden");
  const [text, setText] = useState<string | null>(null);

  useEffect(() => {
    if (!flavor) return;

    const flavorText = flavor;
    const showTimer = window.setTimeout(() => {
      setText(flavorText);
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
  }, [floorNumber, flavor]);

  if (phase === "hidden" || text === null) return null;
  return (
    <div className={`floor-announcer ${phase === "visible" ? "visible" : ""}`}>
      {text}
    </div>
  );
}
