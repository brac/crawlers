using System.Globalization;

namespace Crawlers.Server.Persistence;

/// <summary>
/// Picks a single bleak announcer line for a floor's entry. Pure given
/// (deterministic) inputs and a per-floor RNG seed; templates that
/// require missing data (no killer yet, no deadliest tile yet) are
/// quietly skipped. Falls back to a generic welcome line when nothing
/// has data — fresh worlds shouldn't show "Most common killer: null."
/// </summary>
internal static class FloorFlavorPicker
{
    public static string Pick(
        int floorNumber,
        int floorDeaths,
        string? floorTopKiller,
        int floorTopKillerCount,
        int? deadliestTileX,
        int? deadliestTileY,
        int deadliestTileCount,
        int totalPlayers,
        int totalDeaths,
        double survivalRatePercent)
    {
        // Build the candidate template set against what's actually known.
        // Each candidate is a finished string; only fill the ones whose
        // data exists so the announcer never reads "0 have fallen here."
        var candidates = new List<string>();

        if (totalDeaths > 0 && totalPlayers > 0)
        {
            candidates.Add($"Total crawlers processed: {Format(totalDeaths)}. Survival rate: {survivalRatePercent.ToString("0.0", CultureInfo.InvariantCulture)}%.");
        }
        if (floorDeaths > 0)
        {
            candidates.Add($"{Format(floorDeaths)} have fallen on this floor.");
        }
        if (floorTopKiller is not null && floorTopKillerCount > 0)
        {
            candidates.Add($"Most common cause of death on this floor: {floorTopKiller}.");
        }
        if (deadliestTileX is int dx && deadliestTileY is int dy && deadliestTileCount > 1)
        {
            candidates.Add($"The deadliest tile on this floor sees {Format(deadliestTileCount)} bodies. Tread carefully.");
        }
        if (totalPlayers > 0)
        {
            candidates.Add($"{Format(totalPlayers)} have entered this dungeon. Few have left.");
        }

        if (candidates.Count == 0)
        {
            return $"Floor {floorNumber}. No one has died here yet. That will change.";
        }

        // Deterministic pick keyed off the floor number + an aggregate
        // signal from the data — same flavor for the same floor in the
        // same world state, so refreshing doesn't shuffle the line on
        // every snapshot. Reseeds naturally as the world accumulates
        // deaths.
        var seed = HashCode.Combine(floorNumber, floorDeaths, totalDeaths, deadliestTileCount);
        var idx = (int)((uint)seed % (uint)candidates.Count);
        return candidates[idx];
    }

    private static string Format(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
}
