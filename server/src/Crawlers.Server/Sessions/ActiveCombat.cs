using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Server.Sessions;

/// <summary>
/// Server-side state for an ongoing combat encounter. One enemy versus a
/// dynamic set of player participants — the spec calls for any player on the
/// floor within engagement range to be pulled in, and a teammate who walks
/// into range mid-fight joins via <c>CombatService.AddPlayer</c>.
///
/// Participants leave the fight individually (flee or die) without ending
/// the combat for everyone else. The shared <see cref="Log"/> is broadcast
/// to every participant so the events read identically on every screen.
/// </summary>
public class ActiveCombat
{
    /// <summary>
    /// Floor the combat is taking place on. Combat is scoped to a single
    /// floor — a teammate on a different floor sees no combat updates.
    /// </summary>
    public int FloorNumber { get; init; }

    public Guid EnemyId { get; init; }

    /// <summary>
    /// Player ids currently fighting. Mutated as players flee or die — once
    /// drained the combat resolves with no survivors.
    /// </summary>
    public List<Guid> ParticipantPlayerIds { get; init; } = new();

    /// <summary>
    /// Round-by-round turn order, stable for the duration of the fight.
    /// Holds player ids and the enemy id; the enemy's slot is identified by
    /// equality with <see cref="EnemyId"/>. Late joiners append at the end.
    /// </summary>
    public List<Guid> InitiativeOrder { get; init; } = new();

    public int RoundNumber { get; set; }

    /// <summary>Players who pressed Flee since the last tick. Pop on resolution.</summary>
    public HashSet<Guid> FleeRequested { get; } = new();

    /// <summary>Players who queued an item-use since the last tick. Pop on resolution.</summary>
    public Dictionary<Guid, Guid> UseItemRequested { get; } = new();

    /// <summary>
    /// Per-player terminal outcomes. Keys for players still fighting are
    /// absent; once a player flees, dies, or wins they're written here so the
    /// client can show the right banner. Survivors are stamped PlayerWon
    /// when the enemy falls.
    /// </summary>
    public Dictionary<Guid, CombatOutcome> ParticipantOutcomes { get; } = new();

    public CombatLog Log { get; init; } = new();

    public bool HasParticipant(Guid playerId) =>
        ParticipantPlayerIds.Contains(playerId);
}
