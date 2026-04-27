namespace Crawlers.Domain.Enums;

/// <summary>
/// Terminal outcome of a multi-player run. Stamped on
/// <see cref="Crawlers.Server.Sessions.SessionState"/> by the run-end state
/// transition once the wipe (or future quit) condition is met. Only one
/// member today; reserved values let the future continuation phase add
/// VoluntaryQuit / LastSurvivorQuit without changing the snapshot DTO shape.
/// </summary>
public enum RunOutcome
{
    /// <summary>Every player ended the run in <see cref="GameMode.Resolution"/>.</summary>
    PartyWiped = 0,
    // Reserved (continuation phase — do not implement here):
    //   LastSurvivorQuit = 1,
    //   VoluntaryQuit    = 2,
}
