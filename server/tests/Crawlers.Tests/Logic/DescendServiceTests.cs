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
        var state = BuildExplorationState(seed: 100);
        var svc = new DescendService();

        // Default spawn is on stairs-up; not on stairs-down.
        Assert.False(svc.TryDescend(state, state.PrimaryPlayer.Id));
        Assert.Equal(1, state.PrimaryPlayer.CurrentFloorNumber);
    }

    [Fact]
    public void Rejects_when_in_combat()
    {
        var state = BuildExplorationState(seed: 101);
        // Force the player onto stairs-down so positional check passes…
        var floor = state.GetFloorFor(state.PrimaryPlayer);
        var stairs = FindTile(floor, TileType.StairsDown);
        Assert.True(stairs.HasValue);
        state.PrimaryPlayer.Position = stairs!.Value;
        // …but then enter Combat.
        state.PrimaryPlayer.Mode = GameMode.Combat;
        var combat = new ActiveCombat
        {
            FloorNumber = 1,
            EnemyId = Guid.NewGuid()
        };
        combat.ParticipantPlayerIds.Add(state.PrimaryPlayer.Id);
        state.SetCombat(state.PrimaryPlayer.Id, combat);

        var svc = new DescendService();
        Assert.False(svc.TryDescend(state, state.PrimaryPlayer.Id));
        Assert.Equal(1, state.PrimaryPlayer.CurrentFloorNumber);
    }

    [Fact]
    public void Descend_increments_floor_and_resets_fog_and_carries_state()
    {
        var state = BuildExplorationState(seed: 7);
        // Custom HP and an item to confirm carry-over.
        state.PrimaryPlayer.Stats = state.PrimaryPlayer.Stats with { Hp = 5, MaxHp = 20 };
        state.PrimaryPlayer.Inventory.Add(ItemTemplates.HealingDraught());
        var carriedItemId = state.PrimaryPlayer.Inventory[0].Id;

        var floor1 = state.GetFloorFor(state.PrimaryPlayer);
        var stairsDown = FindTile(floor1, TileType.StairsDown);
        Assert.True(stairsDown.HasValue);
        state.PrimaryPlayer.Position = stairsDown!.Value;
        var floor1Id = floor1.Id;

        var svc = new DescendService();
        Assert.True(svc.TryDescend(state, state.PrimaryPlayer.Id));

        Assert.Equal(2, state.PrimaryPlayer.CurrentFloorNumber);
        var floor2 = state.GetFloorFor(state.PrimaryPlayer);
        Assert.NotEqual(floor1Id, floor2.Id);
        // Player carried over.
        Assert.Equal(5, state.PrimaryPlayer.Stats.Hp);
        Assert.Equal(20, state.PrimaryPlayer.Stats.MaxHp);
        Assert.Single(state.PrimaryPlayer.Inventory);
        Assert.Equal(carriedItemId, state.PrimaryPlayer.Inventory[0].Id);
        // Spawn is on the new floor's stairs-up.
        var newSpawn = FindTile(floor2, TileType.StairsUp);
        Assert.True(newSpawn.HasValue);
        Assert.Equal(newSpawn!.Value, state.PrimaryPlayer.Position);
        // Floor 2 fog is the freshly initialized shared grid for that floor.
        var floor2Fog = state.GetFog(2);
        Assert.NotNull(floor2Fog);
        Assert.Equal(VisibilityState.Visible, floor2Fog![newSpawn.Value.X, newSpawn.Value.Y]);
        // Both floors are now resident in the session, each with its own fog.
        Assert.NotNull(state.GetFloor(1));
        Assert.NotNull(state.GetFloor(2));
        Assert.NotNull(state.GetFog(1));
        Assert.NotNull(state.GetFog(2));
    }

    [Fact]
    public void Descend_uses_deterministic_seed_derivation()
    {
        // Two sessions starting from the same initial seed should produce
        // identical floor 2 layouts when descended.
        var a = BuildExplorationState(seed: 42);
        var b = BuildExplorationState(seed: 42);

        a.PrimaryPlayer.Position = FindTile(a.GetFloorFor(a.PrimaryPlayer), TileType.StairsDown)!.Value;
        b.PrimaryPlayer.Position = FindTile(b.GetFloorFor(b.PrimaryPlayer), TileType.StairsDown)!.Value;

        var svc = new DescendService();
        Assert.True(svc.TryDescend(a, a.PrimaryPlayer.Id));
        Assert.True(svc.TryDescend(b, b.PrimaryPlayer.Id));

        var fa = a.GetFloorFor(a.PrimaryPlayer);
        var fb = b.GetFloorFor(b.PrimaryPlayer);
        Assert.Equal(fa.Width, fb.Width);
        Assert.Equal(fa.Height, fb.Height);
        for (int y = 0; y < fa.Height; y++)
            for (int x = 0; x < fa.Width; x++)
                Assert.Equal(fa.TileGrid[x, y].Type, fb.TileGrid[x, y].Type);
    }

    [Fact]
    public void Two_descenders_do_not_land_on_the_same_tile()
    {
        // Two-player session; both step onto floor 1's stairs-down and descend
        // back-to-back. Without the unoccupied-tile pick they'd both land on
        // floor 2's stairs-up.
        var mgr = new SessionManager();
        var starts = Enumerable.Range(0, 2)
            .Select(_ => new PlayerStartState
            {
                PlayerId = Guid.NewGuid(),
                Stats = SessionManager.DefaultPlayerStats()
            })
            .ToList();
        var state = mgr.CreateSession(starts, seed: 13);
        var floor1 = state.GetFloor(1)!;
        var stairsDown = FindTile(floor1, TileType.StairsDown);
        Assert.True(stairsDown.HasValue);

        // Move both to the stairs-down tile (one at a time — collision rule
        // would block them otherwise).
        state.Players[0].Position = stairsDown!.Value;
        var svc = new DescendService();
        Assert.True(svc.TryDescend(state, state.Players[0].Id));

        state.Players[1].Position = stairsDown.Value;
        Assert.True(svc.TryDescend(state, state.Players[1].Id));

        // Both are on floor 2 now, on distinct tiles.
        Assert.Equal(2, state.Players[0].CurrentFloorNumber);
        Assert.Equal(2, state.Players[1].CurrentFloorNumber);
        Assert.NotEqual(state.Players[0].Position, state.Players[1].Position);
    }

    [Fact]
    public void Descend_bumps_player_DeepestFloorReached()
    {
        // Per-player deepest is the run-summary input — it must rise on
        // descent and stay pinned even if the player later climbs back.
        var state = BuildExplorationState(seed: 19);
        Assert.Equal(1, state.PrimaryPlayer.DeepestFloorReached);

        var floor1 = state.GetFloorFor(state.PrimaryPlayer);
        state.PrimaryPlayer.Position = FindTile(floor1, TileType.StairsDown)!.Value;
        Assert.True(new DescendService().TryDescend(state, state.PrimaryPlayer.Id));

        Assert.Equal(2, state.PrimaryPlayer.CurrentFloorNumber);
        Assert.Equal(2, state.PrimaryPlayer.DeepestFloorReached);
    }

    private static SessionState BuildExplorationState(int seed)
    {
        var mgr = new SessionManager();
        return mgr.CreateSoloSession(seed);
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
