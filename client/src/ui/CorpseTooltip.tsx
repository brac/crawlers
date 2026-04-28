import type { CorpseTooltipInfo } from "../game/DungeonRenderer";

interface CorpseTooltipProps {
  info: CorpseTooltipInfo | null;
  // Page-absolute CSS-pixel coords from the renderer (cursor on desktop,
  // tap point on mobile). Null when not visible.
  clientXY: { x: number; y: number } | null;
  floorNumber: number;
}

export function CorpseTooltip(props: CorpseTooltipProps) {
  if (!props.info || !props.clientXY) return null;
  const name = props.info.username ?? "Unknown wanderer";
  const killer = props.info.killerType;
  const fellLine = killer
    ? `Killed by a ${killer} on floor ${props.floorNumber}`
    : `Fell on floor ${props.floorNumber}`;
  const elapsed = relativeTime(props.info.diedAtIso);
  return (
    <div
      className="corpse-tooltip"
      style={{
        left: props.clientXY.x,
        top: props.clientXY.y,
      }}
      role="tooltip"
    >
      <div className="corpse-tooltip-name">{name}</div>
      <div className="corpse-tooltip-fell">{fellLine}</div>
      {elapsed && <div className="corpse-tooltip-when">{elapsed}</div>}
    </div>
  );
}

/// Humanized "time since". Tracks the dev-tuned alpha tiers in
/// DungeonRenderer.corpseAlpha so the tooltip text aligns with the
/// visual fade — "just now" reads as full-bright, "2 days ago"
/// reads as faded.
function relativeTime(iso: string | null): string | null {
  if (!iso) return null;
  const t = Date.parse(iso);
  if (Number.isNaN(t)) return null;
  const ageMs = Math.max(0, Date.now() - t);
  const sec = Math.round(ageMs / 1000);
  if (sec < 30) return "Just now";
  if (sec < 60) return `${sec} seconds ago`;
  const min = Math.round(sec / 60);
  if (min < 60) return min === 1 ? "1 minute ago" : `${min} minutes ago`;
  const hr = Math.round(min / 60);
  if (hr < 24) return hr === 1 ? "1 hour ago" : `${hr} hours ago`;
  const day = Math.round(hr / 24);
  if (day < 30) return day === 1 ? "1 day ago" : `${day} days ago`;
  const month = Math.round(day / 30);
  if (month < 12) return month === 1 ? "1 month ago" : `${month} months ago`;
  const year = Math.round(month / 12);
  return year === 1 ? "1 year ago" : `${year} years ago`;
}
