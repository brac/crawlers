import type { RunSummaryDto, RunSummaryPlayerDto } from "../api/types";
import { RunOutcome } from "../api/types";

interface RunSummaryProps {
  summary: RunSummaryDto;
  localPlayerId: string;
  onOpenStats: () => void;
}

const TITLE: Record<RunOutcome, string> = {
  [RunOutcome.PartyWiped]: "The party has fallen.",
};

const SUBTITLE: Record<RunOutcome, string> = {
  [RunOutcome.PartyWiped]: "No one made it out.",
};

/// End-of-run overlay shown when snapshot.runSummary is set. Every connected
/// player — living, dead spectating, reconnecting — receives identical
/// content. The Restart button reloads the page (the simplest path back to
/// the lobby; revisit when a "play again" lobby flow ships).
export function RunSummary({ summary, localPlayerId, onOpenStats }: RunSummaryProps) {
  const duration = formatDuration(summary.startedAt, summary.endedAt);
  return (
    <div className="run-summary-overlay">
      <div className="run-summary-card">
        <div className="run-summary-title">☠ {TITLE[summary.outcome]}</div>
        <div className="run-summary-subtitle">{SUBTITLE[summary.outcome]}</div>

        <dl className="run-summary-stats">
          <Stat label="Deepest floor" value={summary.deepestFloor.toString()} />
          <Stat label="Enemies slain" value={summary.enemiesKilled.toString()} />
          <Stat label="Run length" value={duration} />
        </dl>

        <div className="run-summary-section-title">Party</div>
        <ul className="run-summary-roster">
          {summary.players.map((p) => (
            <li
              key={p.playerId}
              className={
                p.playerId === localPlayerId
                  ? "run-summary-row run-summary-row-local"
                  : "run-summary-row"
              }
            >
              <PlayerRow row={p} isLocal={p.playerId === localPlayerId} />
            </li>
          ))}
        </ul>

        <button
          type="button"
          className="run-summary-restart"
          onClick={() => window.location.reload()}
        >
          Back to lobby
        </button>
        <button
          type="button"
          className="run-summary-stats-link"
          onClick={onOpenStats}
        >
          See the world stats →
        </button>
      </div>
    </div>
  );
}

function PlayerRow({
  row,
  isLocal,
}: {
  row: RunSummaryPlayerDto;
  isLocal: boolean;
}) {
  const fate = row.survived
    ? "Survived"
    : (row.causeOfDeath ?? "Fallen in the dark");
  return (
    <>
      <div className="run-summary-row-name">
        {row.username}
        {isLocal ? " (you)" : ""}
      </div>
      <div className="run-summary-row-fate">{fate}</div>
      <div className="run-summary-row-meta">
        Floor {row.deepestFloor} · died at ({row.deathX}, {row.deathY})
      </div>
    </>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="run-summary-stat">
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

function formatDuration(startIso: string, endIso: string): string {
  const ms = Date.parse(endIso) - Date.parse(startIso);
  if (!Number.isFinite(ms) || ms < 0) return "—";
  const totalSec = Math.round(ms / 1000);
  const m = Math.floor(totalSec / 60);
  const s = totalSec % 60;
  return m > 0 ? `${m}m ${s}s` : `${s}s`;
}
