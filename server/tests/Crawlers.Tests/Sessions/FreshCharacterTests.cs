using Crawlers.Domain.Enums;
using Crawlers.Server.Sessions;
using Crawlers.Tests.TestSupport;

namespace Crawlers.Tests.Sessions;

/// <summary>
/// Step 11 of the persistent-world phase: when a player dies and the run
/// ends, "restart" means a brand-new run — no carried-over stats,
/// inventory, or floor. The page reload on the client + a fresh
/// <see cref="SessionManager.CreateSession"/> on the server is the seam.
/// These tests pin the server side of that contract so a future
/// refactor that, say, threads the prior session's inventory into the
/// new <see cref="PlayerStartState"/> would fail loudly here.
/// </summary>
public class FreshCharacterTests
{
    [Fact]
    public void Same_persistent_uuid_starting_a_second_session_gets_fresh_state()
    {
        var corpses = new FakeCorpseService();
        var mgr = new SessionManager(TestWorld.Make(), corpses);
        var pid = Guid.NewGuid(); // the persistent UUID, same across runs

        // Run 1: take damage, pick up an item, descend a floor.
        var first = mgr.CreateSession(new[]
        {
            new PlayerStartState { PlayerId = pid, Stats = SessionManager.DefaultPlayerStats() }
        });
        var p1 = first.PrimaryPlayer;
        p1.Stats = p1.Stats with { Hp = 1 };
        p1.Inventory.Add(Crawlers.Server.Logic.ItemTemplates.HealingDraught());
        p1.CurrentFloorNumber = 5;
        p1.DeepestFloorReached = 5;

        // Run 2: same UUID, fresh PlayerStartState (the lobby bridge always
        // hands fresh defaults — that's the whole shape).
        var second = mgr.CreateSession(new[]
        {
            new PlayerStartState { PlayerId = pid, Stats = SessionManager.DefaultPlayerStats() }
        });
        var p2 = second.PrimaryPlayer;

        Assert.NotSame(first, second);
        Assert.NotEqual(first.Session.Id, second.Session.Id);
        Assert.Equal(pid, p2.Id);
        // Same UUID, brand-new run state.
        Assert.Equal(SessionManager.DefaultPlayerStats().Hp, p2.Stats.Hp);
        Assert.Equal(SessionManager.DefaultPlayerStats().MaxHp, p2.Stats.MaxHp);
        Assert.Empty(p2.Inventory);
        Assert.Equal(1, p2.CurrentFloorNumber);
        Assert.Equal(1, p2.DeepestFloorReached);
        Assert.Equal(GameMode.Exploration, p2.Mode);
        Assert.Null(p2.DiedAt);
        Assert.Null(p2.CauseOfDeath);
    }

    [Fact]
    public void Fresh_session_with_persistent_corpses_starts_floor_1_with_world_history_visible()
    {
        // The world the new character enters is the same canonical world —
        // their own prior corpses (same UUID) hydrate onto floor 1.
        var pid = Guid.NewGuid();
        var corpses = new FakeCorpseService();
        corpses.SetFloor(1, new[]
        {
            new Crawlers.Server.Persistence.CorpseEntry
            {
                Id = Guid.NewGuid(),
                PlayerId = pid,
                FloorNumber = 1,
                X = 5, Y = 5,
                DiedAt = DateTimeOffset.UtcNow.AddHours(-1),
                PlayerUsername = "Brac",
                KillerType = "Husk"
            }
        });
        var mgr = new SessionManager(TestWorld.Make(), corpses);

        var run = mgr.CreateSession(new[]
        {
            new PlayerStartState { PlayerId = pid, Stats = SessionManager.DefaultPlayerStats() }
        });

        var player = run.PrimaryPlayer;
        Assert.Equal(1, player.CurrentFloorNumber);
        Assert.Empty(player.Inventory);

        // The corpse from the prior run hydrates onto the floor — same
        // canonical world, your past attempts still there.
        var ownCorpse = run.GetFloor(1)!.Entities
            .FirstOrDefault(e => e.Type == EntityType.Corpse && e.PlayerId == pid);
        Assert.NotNull(ownCorpse);
    }
}
