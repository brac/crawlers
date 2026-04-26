using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Logic;
using Crawlers.Server.Sessions;
using Xunit;

namespace Crawlers.Tests.Logic;

public class DescendServiceTests
{
    [Fact]
    public void Rejects_when_not_standing_on_stairs_down()
    {
        var state = BuildExplorationState(playerSeed: 100);
        var svc = new DescendService();

        // Default spawn is on stairs-up; not on stairs-down.
        Assert.False(svc.TryDescend(state));
        Assert.Equal(1, state.Session.CurrentFloorNumber);
    }

    [Fact]
    public void Rejects_when_in_combat()
    {
        var state = BuildExplorationState(playerSeed: 101);
        // Force the player onto stairs-down so positional check passes…
        var stairs = FindTile(state.Floor, TileType.StairsDown);
        Assert.True(stairs.HasValue);
        state.Player.Position = stairs!.Value;
        // …but then enter Combat mode.
        state.Session.Mode = GameMode.Combat;

        var svc = new DescendService();
        Assert.False(svc.TryDescend(state));
        Assert.Equal(1, state.Session.CurrentFloorNumber);
    }

    [Fact]
    public void Descend_increments_floor_and_resets_fog_and_carries_state()
    {
        var state = BuildExplorationState(playerSeed: 7);
        // Give the player some custom HP and an item to confirm carry-over.
        state.Player.Stats = state.Player.Stats with { Hp = 5, MaxHp = 20 };
        state.Player.Inventory.Add(ItemTemplates.HealingDraught());
        var carriedItemId = state.Player.Inventory[0].Id;

        var stairsDown = FindTile(state.Floor, TileType.StairsDown);
        Assert.True(stairsDown.HasValue);
        state.Player.Position = stairsDown!.Value;

        var floor1Id = state.Floor.Id;
        var svc = new DescendService();
        Assert.True(svc.TryDescend(state));

        Assert.Equal(2, state.Session.CurrentFloorNumber);
        Assert.NotEqual(floor1Id, state.Floor.Id); // new floor instance
        // Player carried over.
        Assert.Equal(5, state.Player.Stats.Hp);
        Assert.Equal(20, state.Player.Stats.MaxHp);
        Assert.Single(state.Player.Inventory);
        Assert.Equal(carriedItemId, state.Player.Inventory[0].Id);
        // Spawn is on the new floor's stairs-up.
        var newSpawn = FindTile(state.Floor, TileType.StairsUp);
        Assert.True(newSpawn.HasValue);
        Assert.Equal(newSpawn!.Value, state.Player.Position);
        // Fog rebuilt: spawn tile is Visible.
        Assert.NotNull(state.Player.FogOfWar);
        Assert.Equal(VisibilityState.Visible, state.Player.FogOfWar![newSpawn.Value.X, newSpawn.Value.Y]);
        // No stale combat log.
        Assert.Null(state.ActiveCombat);
    }

    [Fact]
    public void Descend_uses_deterministic_seed_derivation()
    {
        // Two sessions starting from the same initial seed should produce
        // identical floor 2 layouts when descended.
        var a = BuildExplorationState(playerSeed: 42);
        var b = BuildExplorationState(playerSeed: 42);

        a.Player.Position = FindTile(a.Floor, TileType.StairsDown)!.Value;
        b.Player.Position = FindTile(b.Floor, TileType.StairsDown)!.Value;

        var svc = new DescendService();
        Assert.True(svc.TryDescend(a));
        Assert.True(svc.TryDescend(b));

        Assert.Equal(a.Floor.Width, b.Floor.Width);
        Assert.Equal(a.Floor.Height, b.Floor.Height);
        for (int y = 0; y < a.Floor.Height; y++)
            for (int x = 0; x < a.Floor.Width; x++)
                Assert.Equal(a.Floor.TileGrid[x, y].Type, b.Floor.TileGrid[x, y].Type);
    }

    private static SessionState BuildExplorationState(int playerSeed)
    {
        var mgr = new SessionManager();
        return mgr.CreateSession(playerSeed);
    }

    private static Position? FindTile(Floor floor, TileType type)
    {
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (floor.TileGrid[x, y].Type == type)
                    return new Position(x, y);
        return null;
    }
}
