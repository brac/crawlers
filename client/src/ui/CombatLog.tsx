import { useEffect, useRef } from "react";
import type { CombatLogDto } from "../api/types";
import { CombatOutcome } from "../api/types";

interface CombatLogProps {
  log: CombatLogDto;
}

const OUTCOME_LABEL: Record<CombatOutcome, string | null> = {
  [CombatOutcome.InProgress]: null,
  [CombatOutcome.PlayerWon]: "Victory",
  [CombatOutcome.PlayerFled]: "Escaped",
  [CombatOutcome.EnemyFled]: "Enemy fled",
  [CombatOutcome.PlayerDied]: "You died",
};

const OUTCOME_CLASS: Record<CombatOutcome, string> = {
  [CombatOutcome.InProgress]: "",
  [CombatOutcome.PlayerWon]: "outcome-win",
  [CombatOutcome.PlayerFled]: "outcome-flee",
  [CombatOutcome.EnemyFled]: "outcome-flee",
  [CombatOutcome.PlayerDied]: "outcome-death",
};

export function CombatLog({ log }: CombatLogProps) {
  const scrollerRef = useRef<HTMLDivElement>(null);

  // Auto-scroll to the latest event whenever the log grows.
  useEffect(() => {
    const el = scrollerRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [log]);

  const outcomeLabel = OUTCOME_LABEL[log.outcome];

  return (
    <div className="combat-log">
      <div className="combat-log-header">
        Combat log
        {outcomeLabel && (
          <span className={`combat-outcome ${OUTCOME_CLASS[log.outcome]}`}>
            {outcomeLabel}
          </span>
        )}
      </div>
      <div ref={scrollerRef} className="combat-log-scroller">
        {log.rounds.map((round) => (
          <div key={round.number} className="combat-round">
            {round.number > 0 && (
              <div className="combat-round-label">Round {round.number}</div>
            )}
            {round.events.map((evt, i) => (
              <div key={i} className="combat-event">
                {evt}
              </div>
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}
