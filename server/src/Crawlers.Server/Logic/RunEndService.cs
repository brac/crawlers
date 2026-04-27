using Crawlers.Domain.Enums;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

/// <summary>
/// Detects whether the run-end state transition should fire and applies it.
///
/// Today only one transition exists: every player in
/// <see cref="GameMode.Resolution"/> → <see cref="RunOutcome.PartyWiped"/>.
/// Future continuation-phase outcomes (LastSurvivorQuit, VoluntaryQuit)
/// will plug in as additional checks before the wipe check; the
/// <c>CheckAndApply</c> shape — pure read of state, side-effect of stamping
/// the outcome on <see cref="SessionState"/> — stays the same.
///
/// Caller holds <see cref="SessionState.SyncRoot"/> so the read of every
/// player's mode is consistent with whatever just mutated it (typically
/// <c>CombatService.MarkPlayerDied</c> on the killing-blow round).
/// </summary>
public class RunEndService
{
    /// <summary>
    /// Returns the outcome that was *newly* applied, or null if the run is
    /// still going or had already ended. The newly-applied signal is what
    /// callers latch onto to fire end-of-run logging / metrics — re-broadcasting
    /// is unconditional because the mapper reads <see cref="SessionState.Outcome"/>
    /// directly.
    /// </summary>
    public RunOutcome? CheckAndApply(SessionState state)
    {
        if (state.IsRunOver) return null;
        if (state.Players.Count == 0) return null;

        // Every player in Resolution → wipe. A disconnected-but-not-dead
        // player keeps the run alive (Mode != Resolution) per the spec:
        // "Disconnected players do not count as dead — the run continues
        // for survivors regardless of how many have disconnected."
        if (state.Players.All(p => p.Mode == GameMode.Resolution))
        {
            state.EndRun(RunOutcome.PartyWiped);
            return RunOutcome.PartyWiped;
        }
        return null;
    }
}
