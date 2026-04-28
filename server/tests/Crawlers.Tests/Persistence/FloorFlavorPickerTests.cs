using Crawlers.Server.Persistence;

namespace Crawlers.Tests.Persistence;

public class FloorFlavorPickerTests
{
    [Fact]
    public void Falls_back_to_generic_line_when_no_data_available()
    {
        // Fresh world: no players, no deaths. Every templated line wants
        // some data — picker must not return e.g. "0 have fallen here."
        var line = FloorFlavorPicker.Pick(
            floorNumber: 3,
            floorDeaths: 0,
            floorTopKiller: null,
            floorTopKillerCount: 0,
            deadliestTileX: null,
            deadliestTileY: null,
            deadliestTileCount: 0,
            totalPlayers: 0,
            totalDeaths: 0,
            survivalRatePercent: 0.0);

        Assert.Equal("Floor 3. No one has died here yet. That will change.", line);
    }

    [Fact]
    public void Picks_a_real_template_when_data_exists()
    {
        // Plenty of data available — picker should return one of the
        // populated templates, never the fallback.
        var line = FloorFlavorPicker.Pick(
            floorNumber: 1,
            floorDeaths: 12,
            floorTopKiller: "Slug",
            floorTopKillerCount: 7,
            deadliestTileX: 8,
            deadliestTileY: 27,
            deadliestTileCount: 5,
            totalPlayers: 100,
            totalDeaths: 250,
            survivalRatePercent: 12.5);

        Assert.DoesNotContain("That will change", line);
        Assert.False(string.IsNullOrWhiteSpace(line));
    }

    [Fact]
    public void Same_inputs_yield_the_same_line()
    {
        // Determinism — the same world state at floor entry should pick
        // the same line, so re-rendering snapshots doesn't reshuffle the
        // announcer.
        var a = FloorFlavorPicker.Pick(2, 5, "Husk", 3, 4, 4, 2, 50, 100, 8.0);
        var b = FloorFlavorPicker.Pick(2, 5, "Husk", 3, 4, 4, 2, 50, 100, 8.0);
        Assert.Equal(a, b);
    }
}
