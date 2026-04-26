import type { GameStateSnapshotDto } from "../api/types";
import { GameMode } from "../api/types";

interface HudProps {
  snapshot: GameStateSnapshotDto | null;
  status: string;
  onRestart: () => void;
  onStairsDown: boolean;
}

const MODE_LABEL: Record<GameMode, string> = {
  [GameMode.Exploration]: "Exploration",
  [GameMode.Combat]: "Combat",
  [GameMode.Resolution]: "Resolution",
};

export function Hud({ snapshot, status, onRestart, onStairsDown }: HudProps) {
  const inCombat = snapshot?.mode === GameMode.Combat;
  const dead = snapshot?.mode === GameMode.Resolution;

  return (
    <>
      <div className="hud">
        <div className="hud-row">
          <span className="hud-label">Status</span>
          <span>{status}</span>
        </div>
        {snapshot && (
          <>
            <div className="hud-row">
              <span className="hud-label">Session</span>
              <span className="hud-mono">{snapshot.sessionId.slice(0, 8)}…</span>
            </div>
            <div className="hud-row">
              <span className="hud-label">Floor</span>
              <span>{snapshot.floorNumber}</span>
            </div>
            <div className="hud-row">
              <span className="hud-label">Mode</span>
              <span
                className={
                  inCombat
                    ? "hud-mode-combat"
                    : dead
                      ? "hud-mode-dead"
                      : undefined
                }
              >
                {MODE_LABEL[snapshot.mode]}
              </span>
            </div>
            <div className="hud-row">
              <span className="hud-label">Position</span>
              <span className="hud-mono">
                ({snapshot.player.x}, {snapshot.player.y})
              </span>
            </div>
            <HpBar hp={snapshot.player.hp} maxHp={snapshot.player.maxHp} />
            <div className="hud-row">
              <span className="hud-label">Visible enemies</span>
              <span>{snapshot.floor.entities.length}</span>
            </div>
          </>
        )}
      </div>
      {inCombat && (
        <div className="combat-banner">
          ⚔ Combat — auto-resolving. Press <kbd>F</kbd> to flee.
        </div>
      )}
      {onStairsDown && !inCombat && !dead && (
        <div className="hint-banner">
          Stairs lead deeper. Press <kbd>&gt;</kbd> to descend.
        </div>
      )}
      {dead && (
        <div className="combat-banner combat-banner-dead">
          <span>☠ You died.</span>
          <button type="button" className="restart-button" onClick={onRestart}>
            Start a new run
          </button>
        </div>
      )}
    </>
  );
}

function HpBar({ hp, maxHp }: { hp: number; maxHp: number }) {
  const pct = maxHp > 0 ? Math.max(0, Math.min(100, (hp / maxHp) * 100)) : 0;
  const danger = pct < 33;
  return (
    <div className="hud-row">
      <span className="hud-label">HP</span>
      <span className="hp-bar" aria-label={`${hp}/${maxHp}`}>
        <span
          className={`hp-bar-fill ${danger ? "hp-low" : ""}`}
          style={{ width: `${pct}%` }}
        />
        <span className="hp-bar-text">
          {hp} / {maxHp}
        </span>
      </span>
    </div>
  );
}
