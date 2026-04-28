namespace Crawlers.Server.Logic;

/// <summary>
/// Step 9 — resolves a display name for any floor number, cycle-aware
/// so endless descent (Step 10/cycle.A) reads narratively. Floor 1-4
/// are the baseline; floors 5-8 prefix "Deeper", 9-12 "Forgotten",
/// etc. Names are placeholders — easy to retune by editing this file.
/// </summary>
public static class FloorNameResolver
{
    private static readonly string[] BaseNames =
    {
        "The Crypt",
        "The Sewers",
        "The Caverns",
        "The Hellscape"
    };

    // Index = cycle. Each entry is a format with `{0}` standing in for
    // the BaseName *with the leading "The " stripped* — so cycle 1 of
    // "The Crypt" becomes "Deeper Crypt", not "Deeper The Crypt".
    private static readonly string[] CyclePrefixes =
    {
        "{0}",                 // cycle 0 — base
        "Deeper {0}",          // cycle 1
        "Forgotten {0}",       // cycle 2
        "Sunken {0}",          // cycle 3
        "Beyond the {0}",      // cycle 4
        "Echoes of the {0}",   // cycle 5
        "Below the {0}"        // cycle 6 and any deeper falls back here
    };

    public static string For(int floorNumber)
    {
        if (floorNumber < 1) floorNumber = 1;
        var cycle = (floorNumber - 1) / BaseNames.Length;
        var baseIdx = (floorNumber - 1) % BaseNames.Length;
        var baseName = BaseNames[baseIdx];

        if (cycle == 0) return baseName;

        // Strip a leading "The " for the prefixed forms so they don't
        // double up: "Deeper The Crypt" → "Deeper Crypt".
        var stripped = baseName.StartsWith("The ", StringComparison.Ordinal)
            ? baseName[4..]
            : baseName;
        var prefixIdx = Math.Min(cycle, CyclePrefixes.Length - 1);
        return string.Format(CyclePrefixes[prefixIdx], stripped);
    }
}
