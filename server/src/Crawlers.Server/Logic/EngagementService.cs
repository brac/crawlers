using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

public class EngagementService
{
    public const int EngagementProximity = 3;

    public Entity? FindEngagement(SessionState state, Guid playerId)
    {
        var player = state.GetPlayer(playerId);
        if (player is null) return null;

        var floor = state.GetFloorFor(player);
        var fog = state.GetFog(floor.FloorNumber);
        if (fog is null) return null;

        var p = player.Position;
        foreach (var e in floor.Entities)
        {
            if (e.Type != EntityType.Enemy) continue;
            if (e.State != EntityState.Alive) continue;

            int dx = Math.Abs(e.Position.X - p.X);
            int dy = Math.Abs(e.Position.Y - p.Y);
            int chebyshev = Math.Max(dx, dy);
            if (chebyshev > EngagementProximity) continue;

            // Shared-fog visibility — proximity is to the moving player but
            // the LOS check is the union of all teammates on this floor. If
            // the enemy is currently visible to anyone, this player engages.
            if (fog[e.Position.X, e.Position.Y] != VisibilityState.Visible)
                continue;

            return e;
        }
        return null;
    }

    /// <summary>
    /// Players on the enemy's floor within engagement proximity. Used at
    /// combat-start to pull every nearby teammate into the fight as
    /// participants — the spec says "combat session can include multiple
    /// players if they're within engagement range on the same floor."
    /// </summary>
    public IReadOnlyList<Player> PlayersInRange(SessionState state, Floor floor, Entity enemy)
    {
        var nearby = new List<Player>();
        foreach (var p in state.PlayersOnFloor(floor.FloorNumber))
        {
            // Spectators and dead players don't engage.
            if (p.Mode == GameMode.Resolution) continue;

            int dx = Math.Abs(p.Position.X - enemy.Position.X);
            int dy = Math.Abs(p.Position.Y - enemy.Position.Y);
            if (Math.Max(dx, dy) <= EngagementProximity)
                nearby.Add(p);
        }
        return nearby;
    }
}
