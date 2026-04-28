import { useEffect, useState } from "react";
import type { GameStateSnapshotDto } from "../api/types";
import { GameMode } from "../api/types";

interface SpectatorOverlayProps {
  snapshot: GameStateSnapshotDto | null;
  onSpectate: (targetId: string) => void;
}

const DEATH_PAUSE_MS = 3000;

/// Phased UI shown while the local player is dead:
///
///   1. 0–3 s after death — a quiet "You have fallen." card. The camera is
///      already held on the corpse because the snapshot's player.x/y is the
///      dead player's last tile (no target chosen yet).
///   2. After the pause — picker listing live + connected teammates. Clicking
///      one invokes SetSpectatorTarget and the snapshot's spectator view
///      kicks in (camera moves to the target).
///   3. While spectating — compact banner showing who's being followed, with
///      a Switch button that re-opens the picker. If the target dies or
///      drops, the server clears the binding and we fall back to step 2.
export function SpectatorOverlay({ snapshot, onSpectate }: SpectatorOverlayProps) {
  const dead = snapshot?.mode === GameMode.Resolution;
  const [pauseElapsed, setPauseElapsed] = useState(false);
  const [pickerOpen, setPickerOpen] = useState(false);

  useEffect(() => {
    if (!dead) {
      setPauseElapsed(false);
      setPickerOpen(false);
      return;
    }
    const handle = window.setTimeout(() => setPauseElapsed(true), DEATH_PAUSE_MS);
    return () => window.clearTimeout(handle);
  }, [dead]);

  if (!snapshot || !dead) return null;

  if (!pauseElapsed) {
    return (
      <div className="spectator-overlay">
        <div className="spectator-card spectator-fallen">
          <div className="spectator-title">☠ You have fallen.</div>
        </div>
      </div>
    );
  }

  const targets = snapshot.spectatableTargets;
  const targetId = snapshot.spectatorTargetId;
  const showPicker = pickerOpen || !targetId;

  if (showPicker) {
    return (
      <div className="spectator-overlay">
        <div className="spectator-card spectator-picker">
          <div className="spectator-title">☠ You have fallen.</div>
          {targets.length === 0 ? (
            <div className="spectator-empty">No teammates left to follow.</div>
          ) : (
            <>
              <div className="spectator-subtitle">Choose someone to spectate:</div>
              <ul className="spectator-list">
                {targets.map((t) => (
                  <li key={t.id}>
                    <button
                      type="button"
                      className={`spectator-target ${t.id === targetId ? "current" : ""}`}
                      onClick={() => {
                        onSpectate(t.id);
                        setPickerOpen(false);
                      }}
                    >
                      <span className="spectator-target-name">{t.username}</span>
                      <span className="spectator-target-meta">
                        Floor {t.floorNumber}
                        {t.inCombat ? " · ⚔" : ""}
                      </span>
                    </button>
                  </li>
                ))}
              </ul>
            </>
          )}
        </div>
      </div>
    );
  }

  // The target may have already disappeared from the picker list (e.g. they
  // descended a floor and the snapshot hasn't refreshed yet). Fall back to a
  // truncated id rather than crashing — the next snapshot will refresh it.
  const targetUsername =
    targets.find((t) => t.id === targetId)?.username ??
    targetId!.slice(0, 4).toUpperCase();

  return (
    <div className="spectator-banner">
      <span>👁 Spectating {targetUsername}</span>
      <button
        type="button"
        className="spectator-switch"
        onClick={() => setPickerOpen(true)}
      >
        Switch
      </button>
    </div>
  );
}
