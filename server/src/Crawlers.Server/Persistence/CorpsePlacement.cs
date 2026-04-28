using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Server.Persistence;

/// <summary>
/// Picks a free, walkable tile for a corpse Entity to render on. Used both
/// when hydrating persistent corpses on floor load and when stamping a
/// fresh corpse on player death.
///
/// <para>
/// Persisted corpse rows always store the <em>true</em> death tile — only
/// the in-memory display position scatters. This keeps the death heatmap /
/// "you died here" data honest while preserving visual readability when
/// many players have died on or near the same tile.
/// </para>
/// </summary>
internal static class CorpsePlacement
{
    /// <summary>
    /// How far we'll wander from the desired tile before giving up and
    /// stacking. Six tiles is enough to absorb dozens of clustered deaths
    /// before any visible stacking returns; anything further would imply
    /// a heatmap-density problem the renderer cap (Step 4) should handle.
    /// </summary>
    private const int MaxScatterRadius = 6;

    public static Position PickFreeTile(Floor floor, Position desired, Random rng)
    {
        var occupied = new HashSet<Position>();
        foreach (var e in floor.Entities)
            if (e.Type == EntityType.Corpse) occupied.Add(e.Position);

        if (IsViable(floor, desired) && !occupied.Contains(desired)) return desired;

        for (int radius = 1; radius <= MaxScatterRadius; radius++)
        {
            // Shuffle each ring so identical floor states don't always
            // displace east first — bodies fan out organically rather
            // than forming a straight string.
            var ring = RingTiles(desired, radius)
                .Where(p => InBounds(floor, p))
                .OrderBy(_ => rng.Next())
                .ToList();
            foreach (var p in ring)
            {
                if (IsViable(floor, p) && !occupied.Contains(p)) return p;
            }
        }
        return desired;
    }

    /// <summary>Chebyshev ring (the perimeter at the given radius).</summary>
    private static IEnumerable<Position> RingTiles(Position center, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
                yield return new Position(center.X + dx, center.Y + dy);
            }
    }

    private static bool InBounds(Floor f, Position p) =>
        p.X >= 0 && p.X < f.Width && p.Y >= 0 && p.Y < f.Height;

    /// <summary>
    /// Walkable tiles a corpse can rest on. Closed/locked doors are
    /// intentionally excluded — a body propping a door open is a gameplay
    /// effect we don't want to model accidentally.
    /// </summary>
    private static bool IsViable(Floor f, Position p)
    {
        if (!InBounds(f, p)) return false;
        var t = f.TileGrid[p.X, p.Y].Type;
        return t == TileType.Floor
            || t == TileType.OpenDoor
            || t == TileType.StairsUp
            || t == TileType.StairsDown;
    }
}
