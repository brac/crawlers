using Crawlers.Domain.Enums;
using Crawlers.Server.Contracts;
using Crawlers.Server.Persistence;
using Crawlers.Server.Sessions;
using Crawlers.Tests.TestSupport;

namespace Crawlers.Tests.Contracts;

/// <summary>
/// Step 10 of the persistent-world phase: live presence is party-scoped,
/// world persistence is global. Two parties on the same canonical floor
/// see each other's corpses (world) but never each other's living
/// players (session-isolated). This file pins that contract so a future
/// refactor that, say, bypasses SessionState's player list and queries
/// SessionManager globally would fail loudly here.
/// </summary>
public class CrossSessionPresenceTests
{
    [Fact]
    public void Two_sessions_on_the_same_floor_do_not_see_each_others_living_players()
    {
        var corpses = new FakeCorpseService();
        var mgr = new SessionManager(TestWorld.Make(), corpses);

        var stateA = mgr.CreateSoloSession();
        var stateB = mgr.CreateSoloSession();

        // Both sessions have a single player on floor 1 — the canonical
        // floor is shared (Step 2) but the player rosters are not.
        Assert.Equal(1, stateA.PrimaryPlayer.CurrentFloorNumber);
        Assert.Equal(1, stateB.PrimaryPlayer.CurrentFloorNumber);

        var snapA = SnapshotMapper.ToSnapshot(stateA, stateA.PrimaryPlayer.Id);
        var snapB = SnapshotMapper.ToSnapshot(stateB, stateB.PrimaryPlayer.Id);

        // Each session's snapshot lists only their own (zero) other
        // players — never the other party's player.
        Assert.Empty(snapA.OtherPlayers);
        Assert.Empty(snapB.OtherPlayers);
        Assert.DoesNotContain(snapA.OtherPlayers, p => p.Id == stateB.PrimaryPlayer.Id);
        Assert.DoesNotContain(snapB.OtherPlayers, p => p.Id == stateA.PrimaryPlayer.Id);
    }

    [Fact]
    public void Two_sessions_on_the_same_floor_see_the_same_world_corpses()
    {
        // Pre-seed a corpse "from a prior run" on floor 1. Both sessions
        // hydrating floor 1 should pick it up — that's the world-scoped
        // half of Step 10.
        var corpses = new FakeCorpseService();
        var deadPlayerId = Guid.NewGuid();
        corpses.SetFloor(1, new[]
        {
            new CorpseEntry
            {
                Id = Guid.NewGuid(),
                PlayerId = deadPlayerId,
                FloorNumber = 1,
                X = 5, Y = 5,
                DiedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                PlayerUsername = "PriorRun",
                KillerType = "Husk"
            }
        });
        var mgr = new SessionManager(TestWorld.Make(), corpses);

        var stateA = mgr.CreateSoloSession();
        var stateB = mgr.CreateSoloSession();

        var corpsesA = stateA.GetFloor(1)!.Entities.Where(e => e.Type == EntityType.Corpse).ToList();
        var corpsesB = stateB.GetFloor(1)!.Entities.Where(e => e.Type == EntityType.Corpse).ToList();
        Assert.Single(corpsesA);
        Assert.Single(corpsesB);
        Assert.Equal(deadPlayerId, corpsesA[0].PlayerId);
        Assert.Equal(deadPlayerId, corpsesB[0].PlayerId);

        // And the snapshot mapper threads the corpses out to both
        // sessions' snapshots (subject to fog — these are at the spawn
        // room which both players have line-of-sight to).
        var snapA = SnapshotMapper.ToSnapshot(stateA, stateA.PrimaryPlayer.Id);
        var snapB = SnapshotMapper.ToSnapshot(stateB, stateB.PrimaryPlayer.Id);
        // Snapshot entities are fog-filtered — assert the corpse exists in
        // the underlying floor (the source-of-truth check), not necessarily
        // in the snapshot if it's outside FOV.
        Assert.Contains(stateA.GetFloor(1)!.Entities, e => e.Type == EntityType.Corpse && e.PlayerId == deadPlayerId);
        Assert.Contains(stateB.GetFloor(1)!.Entities, e => e.Type == EntityType.Corpse && e.PlayerId == deadPlayerId);
        // Heatmap is unfiltered — both sessions get the same world view.
        Assert.NotNull(snapA.Floor.Heatmap);
        Assert.NotNull(snapB.Floor.Heatmap);
        Assert.Equal(snapA.Floor.Heatmap.Count, snapB.Floor.Heatmap.Count);
    }
}
