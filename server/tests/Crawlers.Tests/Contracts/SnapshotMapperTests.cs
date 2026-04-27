using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Contracts;
using Crawlers.Server.Logic;
using Crawlers.Server.Sessions;
using Xunit;

namespace Crawlers.Tests.Contracts;

public class SnapshotMapperTests
{
    [Fact]
    public void Tiles_are_flattened_in_row_major_order()
    {
        var mgr = new SessionManager();
        var state = mgr.CreateSoloSession(seed: 1);
        var snap = SnapshotMapper.ToSnapshot(state, state.PrimaryPlayer.Id);
        var floor = state.GetFloorFor(state.PrimaryPlayer);

        Assert.Equal(floor.Width * floor.Height, snap.Floor.Tiles.Length);
        for (int y = 0; y < floor.Height; y++)
        {
            for (int x = 0; x < floor.Width; x++)
            {
                int expected = (int)floor.TileGrid[x, y].Type;
                int actual = snap.Floor.Tiles[y * floor.Width + x];
                Assert.Equal(expected, actual);
            }
        }
    }

    [Fact]
    public void Snapshot_carries_session_player_and_floor_metadata()
    {
        var mgr = new SessionManager();
        var state = mgr.CreateSoloSession(seed: 1);
        var snap = SnapshotMapper.ToSnapshot(state, state.PrimaryPlayer.Id);
        var floor = state.GetFloorFor(state.PrimaryPlayer);

        Assert.Equal(state.Session.Id, snap.SessionId);
        Assert.Equal(GameMode.Exploration, snap.Mode);
        Assert.Equal(1, snap.FloorNumber);
        Assert.Equal(state.PrimaryPlayer.Id, snap.Player.Id);
        Assert.Equal(state.PrimaryPlayer.Position.X, snap.Player.X);
        Assert.Equal(state.PrimaryPlayer.Position.Y, snap.Player.Y);
        Assert.Equal(floor.Width, snap.Floor.Width);
        Assert.Equal(floor.Height, snap.Floor.Height);
        Assert.Equal(floor.Rooms.Count, snap.Floor.Rooms.Count);
        Assert.Empty(snap.OtherPlayers); // solo session
    }

    [Fact]
    public void OtherPlayers_InCombat_reflects_per_player_mode()
    {
        var mgr = new SessionManager();
        var starts = Enumerable.Range(0, 2)
            .Select(_ => new PlayerStartState
            {
                PlayerId = Guid.NewGuid(),
                Stats = SessionManager.DefaultPlayerStats()
            })
            .ToList();
        var state = mgr.CreateSession(starts, seed: 5);
        var localId = state.Players[0].Id;
        // Flip the teammate into Combat by hand — exercising the snapshot
        // surface, not the engagement flow.
        state.Players[1].Mode = GameMode.Combat;

        var snap = SnapshotMapper.ToSnapshot(state, localId);

        Assert.Single(snap.OtherPlayers);
        Assert.Equal(state.Players[1].Id, snap.OtherPlayers[0].Id);
        Assert.True(snap.OtherPlayers[0].InCombat);
    }

    [Fact]
    public void OtherPlayers_InCombat_is_false_for_exploring_teammates()
    {
        var mgr = new SessionManager();
        var starts = Enumerable.Range(0, 2)
            .Select(_ => new PlayerStartState
            {
                PlayerId = Guid.NewGuid(),
                Stats = SessionManager.DefaultPlayerStats()
            })
            .ToList();
        var state = mgr.CreateSession(starts, seed: 5);

        var snap = SnapshotMapper.ToSnapshot(state, state.Players[0].Id);

        Assert.Single(snap.OtherPlayers);
        Assert.False(snap.OtherPlayers[0].InCombat);
    }

    [Fact]
    public void Per_floor_descent_isolates_snapshots_so_each_player_sees_only_their_floor()
    {
        // Two players start on floor 1. A descends; B stays. After the descent:
        //   - A's snapshot reports floor 2, no other players (B is on floor 1).
        //   - B's snapshot still reports floor 1, with A no longer in
        //     OtherPlayers (A is on floor 2).
        // Combat scope and floor entities are both per-player, so descending
        // a teammate must not bleed into the stayer's view.
        var mgr = new SessionManager();
        var starts = Enumerable.Range(0, 2)
            .Select(_ => new PlayerStartState
            {
                PlayerId = Guid.NewGuid(),
                Stats = SessionManager.DefaultPlayerStats()
            })
            .ToList();
        var state = mgr.CreateSession(starts, seed: 23);
        var a = state.Players[0];
        var b = state.Players[1];

        // Move A to the stairs-down tile on floor 1, then descend.
        var floor1 = state.GetFloorFor(a);
        var stairsDown = FindTile(floor1, TileType.StairsDown);
        Assert.True(stairsDown.HasValue);
        a.Position = stairsDown!.Value;
        Assert.True(new DescendService().TryDescend(state, a.Id));

        var aSnap = SnapshotMapper.ToSnapshot(state, a.Id);
        var bSnap = SnapshotMapper.ToSnapshot(state, b.Id);

        Assert.Equal(2, aSnap.FloorNumber);
        Assert.Empty(aSnap.OtherPlayers); // A is alone on floor 2

        Assert.Equal(1, bSnap.FloorNumber);
        Assert.Empty(bSnap.OtherPlayers); // A no longer visible on floor 1
    }

    [Fact]
    public void Dead_player_with_no_target_lists_alive_connected_teammates_for_picker()
    {
        var mgr = new SessionManager();
        var starts = Enumerable.Range(0, 3)
            .Select(_ => new PlayerStartState
            {
                PlayerId = Guid.NewGuid(),
                Stats = SessionManager.DefaultPlayerStats()
            })
            .ToList();
        var state = mgr.CreateSession(starts, seed: 31);
        var dead = state.Players[0];
        var alive = state.Players[1];
        var disconnected = state.Players[2];

        // Wire connections so only "alive" qualifies as spectatable.
        state.SetConnection(dead.Id, "conn-dead");
        state.SetConnection(alive.Id, "conn-alive");
        // disconnected has no connection → excluded.

        dead.Mode = GameMode.Resolution;

        var snap = SnapshotMapper.ToSnapshot(state, dead.Id);

        Assert.Null(snap.SpectatorTargetId);
        Assert.Single(snap.SpectatableTargets);
        Assert.Equal(alive.Id, snap.SpectatableTargets[0].Id);
        Assert.DoesNotContain(snap.SpectatableTargets, t => t.Id == disconnected.Id);
    }

    [Fact]
    public void Dead_player_with_target_sees_targets_floor_and_position()
    {
        var mgr = new SessionManager();
        var starts = Enumerable.Range(0, 2)
            .Select(_ => new PlayerStartState
            {
                PlayerId = Guid.NewGuid(),
                Stats = SessionManager.DefaultPlayerStats()
            })
            .ToList();
        var state = mgr.CreateSession(starts, seed: 41);
        var dead = state.Players[0];
        var alive = state.Players[1];
        state.SetConnection(dead.Id, "c-dead");
        state.SetConnection(alive.Id, "c-alive");

        // Dead is on floor 1; alive descends to floor 2 to test the
        // cross-floor camera-follow.
        var floor1 = state.GetFloorFor(alive);
        var stairs = FindTile(floor1, TileType.StairsDown);
        Assert.True(stairs.HasValue);
        alive.Position = stairs!.Value;
        Assert.True(new DescendService().TryDescend(state, alive.Id));
        Assert.Equal(2, alive.CurrentFloorNumber);

        // Dead picks alive as their spectate target.
        dead.Mode = GameMode.Resolution;
        dead.SpectatorTargetId = alive.Id;

        var snap = SnapshotMapper.ToSnapshot(state, dead.Id);

        Assert.Equal(alive.Id, snap.SpectatorTargetId);
        // Floor / position reflect the target — across the descent boundary.
        Assert.Equal(alive.CurrentFloorNumber, snap.FloorNumber);
        Assert.Equal(alive.Position.X, snap.Player.X);
        Assert.Equal(alive.Position.Y, snap.Player.Y);
        // Local player's id and hp/max are preserved (it's still the dead player's snapshot).
        Assert.Equal(dead.Id, snap.Player.Id);
        Assert.Equal(GameMode.Resolution, snap.Mode);
    }

    [Fact]
    public void Dead_player_target_is_cleared_when_target_dies()
    {
        var mgr = new SessionManager();
        var starts = Enumerable.Range(0, 2)
            .Select(_ => new PlayerStartState
            {
                PlayerId = Guid.NewGuid(),
                Stats = SessionManager.DefaultPlayerStats()
            })
            .ToList();
        var state = mgr.CreateSession(starts, seed: 53);
        var spectator = state.Players[0];
        var target = state.Players[1];
        state.SetConnection(spectator.Id, "c-s");
        state.SetConnection(target.Id, "c-t");

        spectator.Mode = GameMode.Resolution;
        spectator.SpectatorTargetId = target.Id;
        target.Mode = GameMode.Resolution; // target also dies

        var snap = SnapshotMapper.ToSnapshot(state, spectator.Id);

        Assert.Null(snap.SpectatorTargetId);
        Assert.Null(spectator.SpectatorTargetId); // cleared by mapper
        // Spectator's snapshot now reverts to their own (corpse) view, and
        // since the only teammate also died there's no picker option.
        Assert.Empty(snap.SpectatableTargets);
    }

    private static Position? FindTile(Floor floor, TileType type)
    {
        for (int y = 0; y < floor.Height; y++)
            for (int x = 0; x < floor.Width; x++)
                if (floor.TileGrid[x, y].Type == type)
                    return new Position(x, y);
        return null;
    }

    [Fact]
    public void Ambient_combats_carry_teammate_fights_to_outside_observers()
    {
        // Two players on floor 1. A is fighting an enemy; B is exploring.
        // B's snapshot.combat is null (they're not in any fight) but
        // ambientCombats must include A's combat so the renderer can
        // animate A's swings on B's screen.
        var (state, enemy) = Crawlers.Tests.Logic.CombatServiceTests.BuildTwoPlayer(
            playerAHp: 50, playerBHp: 50, enemyHp: 50);
        var a = state.Players[0];
        var b = state.Players[1];

        var dice = new Crawlers.Tests.Logic.ScriptedDice(d20: new[] { 18, 10, 5 });
        var combat = new Crawlers.Server.Logic.CombatService()
            .Start(state, enemy, new[] { a }, dice);

        var bSnap = SnapshotMapper.ToSnapshot(state, b.Id);

        Assert.Null(bSnap.Combat); // B isn't a participant
        Assert.Single(bSnap.AmbientCombats);
        Assert.Equal(combat.Log.Id, bSnap.AmbientCombats[0].Id);
    }

    [Fact]
    public void Ambient_combats_omits_the_viewers_own_combat()
    {
        // Same setup, but check from A's POV: A's snapshot.combat carries
        // their fight; ambientCombats must be empty (no double-counting).
        var (state, enemy) = Crawlers.Tests.Logic.CombatServiceTests.BuildTwoPlayer(
            playerAHp: 50, playerBHp: 50, enemyHp: 50);
        var a = state.Players[0];

        var dice = new Crawlers.Tests.Logic.ScriptedDice(d20: new[] { 18, 5 });
        new Crawlers.Server.Logic.CombatService()
            .Start(state, enemy, new[] { a }, dice);

        var aSnap = SnapshotMapper.ToSnapshot(state, a.Id);

        Assert.NotNull(aSnap.Combat);
        Assert.Empty(aSnap.AmbientCombats);
    }

    [Fact]
    public void RunSummary_is_null_while_run_is_in_progress()
    {
        var mgr = new SessionManager();
        var state = mgr.CreateSoloSession(seed: 1);

        var snap = SnapshotMapper.ToSnapshot(state, state.PrimaryPlayer.Id);

        Assert.Null(snap.RunSummary);
    }

    [Fact]
    public void RunSummary_appears_for_every_viewer_once_run_has_ended()
    {
        // PartyWiped scenario: two players, both dead. Snapshot for each must
        // carry the same summary — the end-of-run screen renders identically
        // on every client.
        var mgr = new SessionManager();
        var starts = Enumerable.Range(0, 2)
            .Select(_ => new PlayerStartState
            {
                PlayerId = Guid.NewGuid(),
                Stats = SessionManager.DefaultPlayerStats()
            })
            .ToList();
        var state = mgr.CreateSession(starts, seed: 71);
        var p0 = state.Players[0];
        var p1 = state.Players[1];

        // Inject death state by hand; no need to grind a real combat.
        var deathTime = DateTimeOffset.UtcNow;
        p0.Mode = GameMode.Resolution;
        p0.DiedAt = deathTime;
        p0.CauseOfDeath = "Slain by a Husk";
        p1.Mode = GameMode.Resolution;
        p1.DiedAt = deathTime.AddSeconds(2);
        p1.CauseOfDeath = "Slain by a Hulk";
        state.EnemiesKilled = 7;
        state.EndRun(RunOutcome.PartyWiped);

        var snap0 = SnapshotMapper.ToSnapshot(state, p0.Id);
        var snap1 = SnapshotMapper.ToSnapshot(state, p1.Id);

        Assert.NotNull(snap0.RunSummary);
        Assert.NotNull(snap1.RunSummary);

        var summary = snap0.RunSummary!;
        Assert.Equal(RunOutcome.PartyWiped, summary.Outcome);
        Assert.Equal(state.Session.CreatedAt, summary.StartedAt);
        Assert.Equal(state.EndedAt, summary.EndedAt);
        Assert.Equal(7, summary.EnemiesKilled);
        Assert.Equal(2, summary.Players.Count);

        // Both players' rows present, with their specific death info.
        var p0Row = summary.Players.Single(r => r.PlayerId == p0.Id);
        Assert.False(p0Row.Survived);
        Assert.Equal("Slain by a Husk", p0Row.CauseOfDeath);
        Assert.Equal(deathTime, p0Row.DiedAt);

        // Both viewers see the same summary content. We compare component-wise
        // because the embedded Players list is a List<T> (reference-equal only
        // to itself); building the snapshot twice yields two equal-content lists.
        Assert.Equal(snap0.RunSummary!.Outcome, snap1.RunSummary!.Outcome);
        Assert.Equal(snap0.RunSummary.StartedAt, snap1.RunSummary.StartedAt);
        Assert.Equal(snap0.RunSummary.EndedAt, snap1.RunSummary.EndedAt);
        Assert.Equal(snap0.RunSummary.DeepestFloor, snap1.RunSummary.DeepestFloor);
        Assert.Equal(snap0.RunSummary.EnemiesKilled, snap1.RunSummary.EnemiesKilled);
        Assert.Equal(snap0.RunSummary.Players, snap1.RunSummary.Players);
    }

    [Fact]
    public void RunSummary_DeepestFloor_is_max_across_party()
    {
        var mgr = new SessionManager();
        var starts = Enumerable.Range(0, 2)
            .Select(_ => new PlayerStartState
            {
                PlayerId = Guid.NewGuid(),
                Stats = SessionManager.DefaultPlayerStats()
            })
            .ToList();
        var state = mgr.CreateSession(starts, seed: 87);
        // Pretend one of them got further before dying.
        state.Players[0].DeepestFloorReached = 2;
        state.Players[1].DeepestFloorReached = 5;
        state.Players[0].Mode = GameMode.Resolution;
        state.Players[1].Mode = GameMode.Resolution;
        state.EndRun(RunOutcome.PartyWiped);

        var snap = SnapshotMapper.ToSnapshot(state, state.Players[0].Id);

        Assert.NotNull(snap.RunSummary);
        Assert.Equal(5, snap.RunSummary!.DeepestFloor);
        // Per-player rows preserve each player's individual deepest.
        var rowA = snap.RunSummary.Players.Single(r => r.PlayerId == state.Players[0].Id);
        var rowB = snap.RunSummary.Players.Single(r => r.PlayerId == state.Players[1].Id);
        Assert.Equal(2, rowA.DeepestFloor);
        Assert.Equal(5, rowB.DeepestFloor);
    }

    [Fact]
    public void Multi_player_snapshot_lists_teammates_on_same_floor()
    {
        var mgr = new SessionManager();
        var starts = Enumerable.Range(0, 3)
            .Select(_ => new PlayerStartState
            {
                PlayerId = Guid.NewGuid(),
                Stats = SessionManager.DefaultPlayerStats()
            })
            .ToList();
        var state = mgr.CreateSession(starts, seed: 5);

        var pid = state.Players[0].Id;
        var snap = SnapshotMapper.ToSnapshot(state, pid);

        Assert.Equal(pid, snap.Player.Id);
        Assert.Equal(2, snap.OtherPlayers.Count);
        Assert.DoesNotContain(snap.OtherPlayers, o => o.Id == pid);
    }
}
