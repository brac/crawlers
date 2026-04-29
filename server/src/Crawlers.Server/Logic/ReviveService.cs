using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

/// <summary>
/// Multiplayer revive — a living teammate standing adjacent to a dead
/// teammate's corpse can spend 20% of their current HP to bring them
/// back. The dead player must still be in spectator mode (Mode ==
/// Resolution AND still connected). The reviver pays at least 1 HP
/// (the floor is preserved even on low-HP-by-20%-rounding edge cases)
/// but cannot drop themselves below 1 HP — the spec says "if the live
/// player would die from reviving and paying the life tax, they will
/// not die and instead have 1 HP and the revived player will also
/// have 1 HP."
///
/// No limit on revives given or received. The reviver just needs more
/// than 1 HP (a 1-HP reviver can't pay any tax without dying, so the
/// action is rejected outright).
/// </summary>
public class ReviveService
{
    public enum ReviveResult
    {
        Success,
        Rejected
    }

    /// <summary>
    /// Caller holds <see cref="SessionState.SyncRoot"/>.
    /// </summary>
    public ReviveResult TryRevive(SessionState state, Guid liveId, Guid corpseId)
    {
        var live = state.GetPlayer(liveId);
        var dead = state.GetPlayer(corpseId);
        if (live is null || dead is null) return ReviveResult.Rejected;

        // Reviver must be alive.
        if (live.Mode != GameMode.Exploration) return ReviveResult.Rejected;
        // Target must be in spectator mode (dead) and still connected.
        if (dead.Mode != GameMode.Resolution) return ReviveResult.Rejected;
        if (state.GetConnection(corpseId) is null) return ReviveResult.Rejected;
        // Same floor — no cross-floor revives.
        if (live.CurrentFloorNumber != dead.CurrentFloorNumber) return ReviveResult.Rejected;
        // Reviver must have more than 1 HP (a 1-HP reviver can't pay any
        // tax without dying — the rule is "more than 1," not "at least 1").
        if (live.Stats.Hp <= 1) return ReviveResult.Rejected;

        // Find the closest corpse entity belonging to this player on
        // the floor. Persistent-corpse hydration can place past-session
        // corpses with the same PlayerId on the same floor; the live
        // player walks up to ONE of them, so resolve the target by
        // proximity rather than picking the first-found.
        var floor = state.GetFloorFor(live);
        var corpseEntity = floor.Entities
            .Where(e => e.Type == EntityType.Corpse && e.PlayerId == corpseId)
            .OrderBy(e => Chebyshev(e.Position, live.Position))
            .FirstOrDefault();
        if (corpseEntity is null) return ReviveResult.Rejected;

        // Adjacency — Chebyshev ≤ 1 lets the reviver stand on, beside,
        // or diagonally next to the corpse.
        if (Chebyshev(corpseEntity.Position, live.Position) > 1)
            return ReviveResult.Rejected;

        // Compute tax + new HP values per spec.
        var tax = Math.Max(1, (int)Math.Floor(live.Stats.Hp * 0.20));
        int liveNewHp;
        int revivedHp;
        if (live.Stats.Hp - tax < 1)
        {
            // Safety net per spec: the reviver never dies from the
            // revive. Both end up at 1 HP.
            liveNewHp = 1;
            revivedHp = 1;
        }
        else
        {
            liveNewHp = live.Stats.Hp - tax;
            revivedHp = Math.Min(dead.Stats.MaxHp, tax);
        }

        // Apply mutations.
        live.Stats = live.Stats with { Hp = liveNewHp };
        dead.Stats = dead.Stats with { Hp = revivedHp };
        dead.Mode = GameMode.Exploration;
        dead.Position = corpseEntity.Position;
        dead.DiedAt = null;
        dead.CauseOfDeath = null;
        dead.SpectatorTargetId = null;
        // Fresh slate — any bleed/poison that helped kill them doesn't
        // carry over. Standing back up is the moment of reset.
        dead.StatusEffects.Clear();

        // Remove the consumed corpse entity. Past-session corpses
        // elsewhere on the floor stay (they're a different death).
        floor.Entities.Remove(corpseEntity);

        return ReviveResult.Success;
    }

    private static int Chebyshev(Position a, Position b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
