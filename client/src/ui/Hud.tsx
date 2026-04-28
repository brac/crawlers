import type { GameStateSnapshotDto, StatusEffectDto } from "../api/types";
import { GameMode, StatusEffectKind } from "../api/types";

interface HudProps {
  snapshot: GameStateSnapshotDto | null;
  status: string;
  onStairsDown: boolean;
}

const MODE_LABEL: Record<GameMode, string> = {
  [GameMode.Exploration]: "Exploration",
  [GameMode.Combat]: "Combat",
  [GameMode.Resolution]: "Resolution",
};

export function Hud({ snapshot, status, onStairsDown }: HudProps) {
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
            {snapshot.player.statusEffects.length > 0 && (
              <div className="hud-row">
                <span className="hud-label">Status</span>
                <span className="hud-status-badges">
                  {snapshot.player.statusEffects.map((s, i) => (
                    <StatusBadge key={i} status={s} />
                  ))}
                </span>
              </div>
            )}
            {snapshot.player.equippedWeaponName && (
              <div className="hud-row">
                <span className="hud-label">Wielding</span>
                <span>
                  {snapshot.player.equippedWeaponName}
                  {snapshot.player.equippedWeapon && (
                    <span className="hud-weapon-stats">
                      {" "}
                      ({formatDice(snapshot.player.equippedWeapon.damage)} dmg
                      {" / "}
                      {formatInit(snapshot.player.equippedWeapon.initiativeMod)} init)
                    </span>
                  )}
                </span>
              </div>
            )}
            <div className="hud-row">
              <span className="hud-label">Gold</span>
              <span className="hud-gold">◉ {snapshot.player.gold}</span>
            </div>
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
      {/* Death banner / spectator UI lives in <SpectatorOverlay /> in Game.tsx. */}
    </>
  );
}

function StatusBadge({ status }: { status: StatusEffectDto }) {
  const isBleed = status.kind === StatusEffectKind.Bleed;
  const label = isBleed ? "Bleed" : "Poison";
  const glyph = isBleed ? "🩸" : "☠";
  return (
    <span
      className={`hud-status-badge ${isBleed ? "bleed" : "poison"}`}
      title={`${label} — ${status.damagePerTick} dmg / round, ${status.roundsRemaining} round(s) left`}
    >
      {glyph} {status.roundsRemaining}
    </span>
  );
}

function formatDice(d: { count: number; sides: number; modifier: number }): string {
  const dice = `${d.count}d${d.sides}`;
  if (d.modifier === 0) return dice;
  return d.modifier > 0 ? `${dice}+${d.modifier}` : `${dice}${d.modifier}`;
}

function formatInit(mod: number): string {
  return mod > 0 ? `+${mod}` : `${mod}`;
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
