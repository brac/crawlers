import { useEffect, useState } from "react";
import type { WorldStatsDto } from "../api/types";

interface WorldStatsProps {
  onBack: () => void;
}

type Status =
  | { kind: "loading" }
  | { kind: "ready"; stats: WorldStatsDto }
  | { kind: "error"; message: string };

/// Step 12 — public stats page. Hits GET /api/world-stats and renders the
/// aggregate world history as a card grid. No auth, no identity required —
/// anyone visiting can see the graveyard's totals.
export function WorldStats({ onBack }: WorldStatsProps) {
  const [status, setStatus] = useState<Status>({ kind: "loading" });

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const res = await fetch("/api/world-stats");
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const stats = (await res.json()) as WorldStatsDto;
        if (cancelled) return;
        setStatus({ kind: "ready", stats });
      } catch (err) {
        if (cancelled) return;
        const message = err instanceof Error ? err.message : String(err);
        setStatus({ kind: "error", message });
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <div className="lobby-screen">
      <div className="world-stats-card">
        <div className="lobby-title">CRAWLERS</div>
        <div className="world-stats-subtitle">The graveyard, by the numbers</div>

        {status.kind === "loading" && (
          <div className="lobby-spinner-text">Loading…</div>
        )}
        {status.kind === "error" && (
          <div className="lobby-error">Failed to load stats: {status.message}</div>
        )}
        {status.kind === "ready" && <StatsGrid stats={status.stats} />}

        <button type="button" className="lobby-secondary" onClick={onBack}>
          Back
        </button>
      </div>
    </div>
  );
}

function StatsGrid({ stats }: { stats: WorldStatsDto }) {
  return (
    <div className="world-stats-grid">
      <Stat
        label="Total players"
        value={fmt(stats.totalPlayers)}
        sub="Identified souls who entered"
      />
      <Stat
        label="Total deaths"
        value={fmt(stats.totalDeaths)}
        sub="Bodies on the floor"
      />
      <Stat
        label="Deepest floor reached"
        value={stats.deepestFloorReached.toString()}
        sub="The high-water mark of the dead"
      />
      <Stat
        label="Survival rate"
        value={`${stats.survivalRatePercent.toFixed(1)}%`}
        sub="Players who haven't died — yet"
      />
      <Stat
        label="Average death floor"
        value={
          stats.averageFloorAtDeath > 0
            ? stats.averageFloorAtDeath.toFixed(1)
            : "—"
        }
        sub="Where the typical run ends"
      />
      <Stat
        label="Most common killer"
        value={stats.mostCommonKiller?.killer ?? "—"}
        sub={
          stats.mostCommonKiller
            ? `${fmt(stats.mostCommonKiller.count)} kills`
            : "No kills yet"
        }
      />
      <Stat
        label="Deadliest tile"
        value={
          stats.deadliestTile
            ? `Floor ${stats.deadliestTile.floorNumber} · (${stats.deadliestTile.x}, ${stats.deadliestTile.y})`
            : "—"
        }
        sub={
          stats.deadliestTile
            ? `${fmt(stats.deadliestTile.count)} have fallen here`
            : "No data"
        }
      />
      <Stat
        label="Most-fallen player"
        value={stats.mostFallenPlayer?.username ?? "—"}
        sub={
          stats.mostFallenPlayer
            ? `Died ${fmt(stats.mostFallenPlayer.count)} times`
            : "No data"
        }
      />
    </div>
  );
}

function Stat({ label, value, sub }: { label: string; value: string; sub: string }) {
  return (
    <div className="world-stats-stat">
      <div className="world-stats-stat-label">{label}</div>
      <div className="world-stats-stat-value">{value}</div>
      <div className="world-stats-stat-sub">{sub}</div>
    </div>
  );
}

function fmt(n: number): string {
  return n.toLocaleString();
}
