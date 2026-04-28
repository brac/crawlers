namespace Crawlers.Server.Persistence;

/// <summary>
/// Knobs that govern the canonical persisted world. These are intentionally
/// constants in code rather than configuration — bumping them is a deliberate
/// "I want to change the dungeon" act, and the change should land in a commit.
/// </summary>
public static class WorldConstants
{
    /// <summary>
    /// Dungeon-version stamp written to every <see cref="FloorRecord"/> at
    /// mint time. Bump when a code change alters floor generation in a way
    /// that should invalidate existing floors — a BSP rule change, a new
    /// tile type that doesn't render in old maps, a structural pass that
    /// would shift door coordinates. Bumping wipes the floors table AND
    /// every corpse keyed to one of those floors, since the spatial
    /// coordinates would no longer map to anything coherent in the
    /// regenerated dungeon (PERSISTENT_WORLD.md edge case).
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// How many floors the world-mint pass generates at startup. Players
    /// who descend beyond this number trigger a lazy mint of the missing
    /// floor at first visit. Tuned to match the gameplay cap (descent stops
    /// emitting a meaningful enemy bump past ~10 floors).
    /// </summary>
    public const int InitialFloorCount = 10;

    /// <summary>
    /// Per-floor render cap on persistent corpses (Step 4). Older corpses
    /// stay in the DB — heatmap and stats queries see them — but the
    /// hydrator only stamps the most recent N onto the in-memory floor.
    /// Beyond this, the visual graveyard becomes unreadable and per-tile
    /// scatter starts piling up at the perimeter.
    /// </summary>
    public const int MaxRenderedCorpsesPerFloor = 100;

    /// <summary>
    /// Per-floor cap on heatmap tiles shipped to the client (Step 9). Even
    /// a wildly populated world won't dim more than this many tiles. The
    /// query returns the hottest first so the cap drops the long tail of
    /// 1- and 2-death tiles when the floor is genuinely saturated.
    /// </summary>
    public const int MaxHeatmapTilesPerFloor = 256;
}
