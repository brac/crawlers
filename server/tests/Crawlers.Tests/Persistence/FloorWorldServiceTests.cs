using Crawlers.Domain.Enums;
using Crawlers.Server.Persistence;
using Crawlers.Tests.TestSupport;

namespace Crawlers.Tests.Persistence;

public class FloorWorldServiceTests
{
    [Fact]
    public void Two_sessions_loading_same_floor_see_same_geometry_and_seed()
    {
        var world = TestWorld.Make(baseSeed: 1234);
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();

        var floorA = world.LoadFloorForSession(1, sessionA);
        var floorB = world.LoadFloorForSession(1, sessionB);

        // Canonical identity: both sessions point at the same world floor.
        Assert.Equal(floorA.Id, floorB.Id);
        Assert.Equal(floorA.Seed, floorB.Seed);
        Assert.Equal(1234, floorA.Seed);

        // Per-session isolation: tile grids are separate arrays so a door
        // bump in session A doesn't bleed into session B.
        Assert.NotSame(floorA.TileGrid, floorB.TileGrid);
        Assert.Equal(floorA.Width, floorB.Width);
        Assert.Equal(floorA.Height, floorB.Height);
        for (int y = 0; y < floorA.Height; y++)
            for (int x = 0; x < floorA.Width; x++)
                Assert.Equal(floorA.TileGrid[x, y].Type, floorB.TileGrid[x, y].Type);

        // Per-session SessionId on each Floor.
        Assert.Equal(sessionA, floorA.SessionId);
        Assert.Equal(sessionB, floorB.SessionId);
    }

    [Fact]
    public void Door_bump_in_one_session_does_not_leak_to_another()
    {
        var world = TestWorld.Make(baseSeed: 99);
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();

        var floorA = world.LoadFloorForSession(1, sessionA);
        // Find a door tile and "open" it in session A.
        var (dx, dy) = FindFirstTile(floorA, TileType.Door)
            ?? throw new InvalidOperationException("Floor 1 fixture has no closed door — generator change?");
        floorA.TileGrid[dx, dy] = new Crawlers.Domain.Models.Tile(TileType.OpenDoor);

        var floorB = world.LoadFloorForSession(1, sessionB);
        Assert.Equal(TileType.Door, floorB.TileGrid[dx, dy].Type);
    }

    [Fact]
    public void Lazy_mint_handles_floors_past_initial_range()
    {
        var world = TestWorld.Make();

        // Floor 50 was never minted via MintAsync — first load triggers it.
        var floor = world.LoadFloorForSession(50, Guid.NewGuid());

        Assert.Equal(50, floor.FloorNumber);
        Assert.True(floor.Width > 0);
        Assert.True(floor.Height > 0);
    }

    [Fact]
    public async Task MintAsync_populates_initial_floor_count()
    {
        var world = TestWorld.Make();

        await world.MintAsync(WorldConstants.InitialFloorCount);

        // After mint, every floor 1..N is loadable without further generation.
        for (int n = 1; n <= WorldConstants.InitialFloorCount; n++)
        {
            var floor = world.LoadFloorForSession(n, Guid.NewGuid());
            Assert.Equal(n, floor.FloorNumber);
        }
    }

    private static (int X, int Y)? FindFirstTile(Crawlers.Domain.Models.Floor floor, TileType type)
    {
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (floor.TileGrid[x, y].Type == type)
                    return (x, y);
        return null;
    }
}
