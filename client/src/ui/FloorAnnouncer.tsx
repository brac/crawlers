import { useEffect, useRef, useState } from "react";

interface FloorAnnouncerProps {
  floorNumber: number;
  flavor: string | null;
}

const VISIBLE_MS = 10_000; // hold time before fade-out
const FADE_OUT_MS = 600; // matches the CSS transition

/// Step 12 — bleak announcer line shown briefly at the bottom of the
/// screen on every floor change. Hooks the floorNumber as the trigger:
/// when it changes, the new flavor (server-picked, frozen per floor)
/// fades in, holds, fades out. Suppresses itself on first mount so the
/// initial join doesn't double up with the lobby → game transition,
/// then re-fires on every subsequent descent.
export function FloorAnnouncer({ floorNumber, flavor }: FloorAnnouncerProps) {
  const [shown, setShown] = useState<{ floor: number; text: string } | null>(null);
  const [visible, setVisible] = useState(false);
  const lastFloorRef = useRef<number | null>(null);
  const hideTimerRef = useRef<number | null>(null);
  const clearTimerRef = useRef<number | null>(null);

  useEffect(() => {
    // Same floor — nothing to do. (Snapshots arrive every move; we only
    // re-fire on actual floor change.)
    if (lastFloorRef.current === floorNumber) return;
    const isFirst = lastFloorRef.current === null;
    lastFloorRef.current = floorNumber;
    if (!flavor) return;

    setShown({ floor: floorNumber, text: flavor });
    setVisible(true);

    // Clear any in-flight timers from the previous floor's announcement.
    if (hideTimerRef.current !== null) window.clearTimeout(hideTimerRef.current);
    if (clearTimerRef.current !== null) window.clearTimeout(clearTimerRef.current);

    // First mount: hold a touch shorter so the player isn't waiting on
    // text to clear before they get oriented.
    const hold = isFirst ? VISIBLE_MS - 1000 : VISIBLE_MS;
    hideTimerRef.current = window.setTimeout(() => setVisible(false), hold);
    clearTimerRef.current = window.setTimeout(
      () => setShown(null),
      hold + FADE_OUT_MS,
    );

    return () => {
      if (hideTimerRef.current !== null) window.clearTimeout(hideTimerRef.current);
      if (clearTimerRef.current !== null) window.clearTimeout(clearTimerRef.current);
    };
  }, [floorNumber, flavor]);

  if (!shown) return null;
  return (
    <div className={`floor-announcer ${visible ? "visible" : ""}`}>
      {shown.text}
    </div>
  );
}
