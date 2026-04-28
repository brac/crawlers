using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Sessions;
using Xunit;
using Crawlers.Tests.TestSupport;

namespace Crawlers.Tests.Sessions;

public class SessionManagerTests
{
    [Fact]
    public void CreateSoloSession_returns_state_with_floor_and_one_player()
    {
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        var state = mgr.CreateSoloSession();

        Assert.NotEqual(Guid.Empty, state.Session.Id);
        Assert.Single(state.Players);
        var p = state.PrimaryPlayer;
        Assert.Equal(state.Session.Id, p.SessionId);
        Assert.Equal(1, p.CurrentFloorNumber);
        Assert.Equal(GameMode.Exploration, p.Mode);
        var floor = state.GetFloorFor(p);
        Assert.NotNull(floor);
        Assert.Equal(1, floor.FloorNumber);
    }

    [Fact]
    public void Solo_player_starts_on_stairs_up_tile()
    {
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        var state = mgr.CreateSoloSession();

        var p = state.PrimaryPlayer;
        var floor = state.GetFloorFor(p);
        Assert.Equal(TileType.StairsUp, floor.TileGrid[p.Position.X, p.Position.Y].Type);
    }

    [Fact]
    public void Sessions_have_unique_ids_and_independent_state()
    {
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        var a = mgr.CreateSoloSession();
        var b = mgr.CreateSoloSession();

        Assert.NotEqual(a.Session.Id, b.Session.Id);
        Assert.NotEqual(a.PrimaryPlayer.Id, b.PrimaryPlayer.Id);
        // Canonical floor identity is *shared* across sessions (Step 2 of the
        // persistent-world phase) — both sessions point at the same world.
        // Independence is now expressed in the cloned tile grid + per-session
        // entity list, not the floor id.
        var fa = a.GetFloorFor(a.PrimaryPlayer);
        var fb = b.GetFloorFor(b.PrimaryPlayer);
        Assert.Equal(fa.Id, fb.Id);
        Assert.NotSame(fa.TileGrid, fb.TileGrid);
        Assert.NotSame(fa.Entities, fb.Entities);
        Assert.Equal(2, mgr.ActiveCount);
    }

    [Fact]
    public void Get_returns_same_state_instance()
    {
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        var created = mgr.CreateSoloSession();

        var fetched = mgr.Get(created.Session.Id);
        Assert.Same(created, fetched);
    }

    [Fact]
    public void Get_returns_null_for_unknown_session()
    {
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        Assert.Null(mgr.Get(Guid.NewGuid()));
    }

    [Fact]
    public void Remove_drops_session_and_count_decreases()
    {
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        var state = mgr.CreateSoloSession();
        Assert.Equal(1, mgr.ActiveCount);

        Assert.True(mgr.Remove(state.Session.Id));
        Assert.Equal(0, mgr.ActiveCount);
        Assert.Null(mgr.Get(state.Session.Id));
    }

    [Fact]
    public void Default_player_stats_have_sight_radius_5_per_spec()
    {
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        var state = mgr.CreateSoloSession();
        Assert.Equal(5, state.PrimaryPlayer.Stats.SightRadius);
    }

    [Fact]
    public void Multi_player_session_seats_each_player_on_a_distinct_walkable_tile()
    {
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        var starts = Enumerable.Range(0, 4)
            .Select(_ => new PlayerStartState
            {
                PlayerId = Guid.NewGuid(),
                Stats = SessionManager.DefaultPlayerStats()
            })
            .ToList();

        var state = mgr.CreateSession(starts);

        Assert.Equal(4, state.Players.Count);
        var positions = state.Players.Select(p => p.Position).ToList();
        Assert.Equal(positions.Count, positions.Distinct().Count());
        // All players start on floor 1.
        Assert.All(state.Players, p => Assert.Equal(1, p.CurrentFloorNumber));
        // First player is on the stairs-up tile (the spawn anchor).
        var floor = state.GetFloorFor(state.Players[0]);
        var anchor = state.Players[0].Position;
        Assert.Equal(TileType.StairsUp, floor.TileGrid[anchor.X, anchor.Y].Type);
    }

    [Fact]
    public void Session_starts_with_only_floor_1_loaded()
    {
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        var state = mgr.CreateSoloSession();

        Assert.Single(state.Floors);
        Assert.True(state.Floors.ContainsKey(1));
        Assert.Null(state.GetFloor(2));
    }

    [Fact]
    public void AddPlayerToSession_seats_late_joiner_on_floor_1_at_an_unoccupied_tile()
    {
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        var state = mgr.CreateSoloSession();
        var existingPos = state.PrimaryPlayer.Position;

        var newId = Guid.NewGuid();
        var added = mgr.AddPlayerToSession(state.Session.Id, new PlayerStartState
        {
            PlayerId = newId,
            Stats = SessionManager.DefaultPlayerStats()
        });

        Assert.NotNull(added);
        Assert.Equal(newId, added!.Id);
        Assert.Equal(state.Session.Id, added.SessionId);
        Assert.Equal(1, added.CurrentFloorNumber);
        Assert.NotEqual(existingPos, added.Position);
        Assert.Equal(2, state.Players.Count);
        Assert.Same(added, state.GetPlayer(newId));
    }

    [Fact]
    public void AddPlayerToSession_is_idempotent_for_the_same_player_id()
    {
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        var state = mgr.CreateSoloSession();
        var existingId = state.PrimaryPlayer.Id;
        var beforeCount = state.Players.Count;

        var second = mgr.AddPlayerToSession(state.Session.Id, new PlayerStartState
        {
            PlayerId = existingId,
            Stats = SessionManager.DefaultPlayerStats()
        });

        Assert.NotNull(second);
        Assert.Same(state.PrimaryPlayer, second);
        Assert.Equal(beforeCount, state.Players.Count);
    }

    [Fact]
    public void Shared_fog_is_per_floor_and_unions_each_players_LOS()
    {
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        var starts = Enumerable.Range(0, 2)
            .Select(_ => new PlayerStartState
            {
                PlayerId = Guid.NewGuid(),
                Stats = SessionManager.DefaultPlayerStats()
            })
            .ToList();
        var state = mgr.CreateSession(starts);

        var fog = state.GetFog(1)!;
        // Both players' tiles must be Visible — the shared fog is the union
        // of both LOS cones.
        foreach (var p in state.Players)
            Assert.Equal(VisibilityState.Visible, fog[p.Position.X, p.Position.Y]);
    }

    [Fact]
    public void Late_joiner_contributes_to_existing_floors_shared_fog_without_clobbering()
    {
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        var state = mgr.CreateSoloSession();
        var fog = state.GetFog(1)!;
        var hostTile = state.PrimaryPlayer.Position;
        Assert.Equal(VisibilityState.Visible, fog[hostTile.X, hostTile.Y]);

        var newId = Guid.NewGuid();
        var added = mgr.AddPlayerToSession(state.Session.Id, new PlayerStartState
        {
            PlayerId = newId,
            Stats = SessionManager.DefaultPlayerStats()
        });
        Assert.NotNull(added);

        // Host's tile is still Visible — adding a teammate doesn't demote
        // tiles the host can still see.
        Assert.Equal(VisibilityState.Visible, state.GetFog(1)![hostTile.X, hostTile.Y]);
        // And the late-joiner's tile is now Visible too.
        Assert.Equal(VisibilityState.Visible, state.GetFog(1)![added!.Position.X, added.Position.Y]);
    }

    [Fact]
    public void Multi_floor_session_creation_seats_each_cohort_on_their_starting_floor()
    {
        // One player begins on floor 1 (fresh-run), the other on floor 3 (the
        // continuation case the spec's integration note calls out). Both
        // floors must be generated with the deterministic seed and each
        // player must land on their stairs-up tile on the right floor.
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        var floor1Start = new PlayerStartState
        {
            PlayerId = Guid.NewGuid(),
            Stats = SessionManager.DefaultPlayerStats(),
            FloorNumber = 1
        };
        var floor3Start = new PlayerStartState
        {
            PlayerId = Guid.NewGuid(),
            Stats = SessionManager.DefaultPlayerStats(),
            FloorNumber = 3
        };

        var state = mgr.CreateSession(new[] { floor1Start, floor3Start });

        Assert.Equal(2, state.Players.Count);
        Assert.NotNull(state.GetFloor(1));
        Assert.NotNull(state.GetFloor(3));
        Assert.Null(state.GetFloor(2));   // floor 2 not generated — no one started there

        var p1 = state.GetPlayer(floor1Start.PlayerId)!;
        var p3 = state.GetPlayer(floor3Start.PlayerId)!;
        Assert.Equal(1, p1.CurrentFloorNumber);
        Assert.Equal(3, p3.CurrentFloorNumber);

        var floor1 = state.GetFloorFor(p1);
        var floor3 = state.GetFloorFor(p3);
        Assert.Equal(TileType.StairsUp, floor1.TileGrid[p1.Position.X, p1.Position.Y].Type);
        Assert.Equal(TileType.StairsUp, floor3.TileGrid[p3.Position.X, p3.Position.Y].Type);
    }

    [Fact]
    public void AddPlayerToSession_can_seat_a_late_joiner_on_a_non_floor_one_floor()
    {
        // Continuation use case: the session has been running on floor 1, and
        // a player whose saved state pinned them to floor 4 joins via code.
        // AddPlayerToSession honours start.FloorNumber.
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        var state = mgr.CreateSoloSession();
        Assert.Null(state.GetFloor(4));

        var newId = Guid.NewGuid();
        var added = mgr.AddPlayerToSession(state.Session.Id, new PlayerStartState
        {
            PlayerId = newId,
            Stats = SessionManager.DefaultPlayerStats(),
            FloorNumber = 4
        });

        Assert.NotNull(added);
        Assert.Equal(4, added!.CurrentFloorNumber);
        Assert.NotNull(state.GetFloor(4));
        var floor4 = state.GetFloorFor(added);
        Assert.Equal(TileType.StairsUp, floor4.TileGrid[added.Position.X, added.Position.Y].Type);
    }

    [Fact]
    public void GetCombatByEnemy_skips_finalized_combats_so_a_lingering_dead_one_doesnt_shadow_the_live_one()
    {
        var session = new Session { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        var state = new SessionState(session);

        var enemyId = Guid.NewGuid();
        var ended = new ActiveCombat
        {
            EnemyId = enemyId,
            Log = new CombatLog { Outcome = CombatOutcome.PlayerDied }
        };
        state.SetCombat(Guid.NewGuid(), ended);

        // Only ended combat present — by-enemy lookup hides it.
        Assert.Null(state.GetCombatByEnemy(enemyId));

        var live = new ActiveCombat
        {
            EnemyId = enemyId,
            Log = new CombatLog { Outcome = CombatOutcome.InProgress }
        };
        state.SetCombat(Guid.NewGuid(), live);

        // Both registered to the same enemy id — InProgress wins.
        Assert.Same(live, state.GetCombatByEnemy(enemyId));
    }

    [Fact]
    public void AddPlayerToSession_returns_null_for_unknown_session()
    {
        var mgr = new SessionManager(TestWorld.Make(), TestWorld.MakeCorpses());
        var added = mgr.AddPlayerToSession(Guid.NewGuid(), new PlayerStartState
        {
            PlayerId = Guid.NewGuid(),
            Stats = SessionManager.DefaultPlayerStats()
        });
        Assert.Null(added);
    }
}
