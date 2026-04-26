using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Sessions;

namespace Crawlers.Server.Logic;

public class EngagementService
{
    public const int EngagementProximity = 3;

    public Entity? FindEngagement(SessionState state)
    {
        if (state.Player.FogOfWar is null) return null;
        var p = state.Player.Position;

        foreach (var e in state.Floor.Entities)
        {
            if (e.Type != EntityType.Enemy) continue;
            if (e.State != EntityState.Alive) continue;

            int dx = Math.Abs(e.Position.X - p.X);
            int dy = Math.Abs(e.Position.Y - p.Y);
            int chebyshev = Math.Max(dx, dy);
            if (chebyshev > EngagementProximity) continue;

            // Enemy must be in player's current LOS (visible right now).
            if (state.Player.FogOfWar[e.Position.X, e.Position.Y] != VisibilityState.Visible)
                continue;

            return e;
        }
        return null;
    }
}
