using Crawlers.Domain.Enums;
using Crawlers.Server.Persistence;
using Crawlers.Server.Sessions;
using Crawlers.Tests.TestSupport;

namespace Crawlers.Tests.Persistence;

public class PersistentCorpseHydratorTests
{
    [Fact]
    public void SessionManager_hydrates_corpses_on_floor_load()
    {
        // Two persistent corpses already in the DB on floor 1, then a fresh
        // session loads floor 1 — they should appear as in-memory Corpse
        // entities in floor.Entities so the snapshot mapper renders them.
        var fakeCorpses = new FakeCorpseService();
        var floor1Corpses = new[]
        {
            new CorpseEntry
            {
                Id = Guid.NewGuid(),
                PlayerId = Guid.NewGuid(),
                FloorNumber = 1,
                X = 5, Y = 5,
                DiedAt = DateTimeOffset.UtcNow,
                PlayerUsername = "Brac",
                CauseOfDeath = "Slain by a Husk",
                KillerType = "Husk",
                DeepestFloor = 1
            },
            new CorpseEntry
            {
                Id = Guid.NewGuid(),
                PlayerId = Guid.NewGuid(),
                FloorNumber = 1,
                X = 6, Y = 6,
                DiedAt = DateTimeOffset.UtcNow,
                PlayerUsername = "Steve",
                KillerType = "Hulk",
                DeepestFloor = 1
            }
        };
        fakeCorpses.SetFloor(1, floor1Corpses);

        var mgr = new SessionManager(TestWorld.Make(), fakeCorpses);
        var state = mgr.CreateSoloSession();
        var floor = state.GetFloor(1)!;

        var corpseEntities = floor.Entities
            .Where(e => e.Type == EntityType.Corpse)
            .ToList();
        Assert.Equal(2, corpseEntities.Count);
        Assert.Contains(corpseEntities, e =>
            e.Position.X == 5 && e.Position.Y == 5 && e.PlayerId == floor1Corpses[0].PlayerId);
        Assert.Contains(corpseEntities, e =>
            e.Position.X == 6 && e.Position.Y == 6 && e.PlayerId == floor1Corpses[1].PlayerId);
    }

    [Fact]
    public void Hydrator_skips_out_of_bounds_corpses()
    {
        // Defensive: a row with out-of-range coords (manual edit, schema
        // drift, etc.) is logged-and-skipped rather than crashing the floor
        // load. Only the in-bounds row hydrates.
        var fakeCorpses = new FakeCorpseService();
        fakeCorpses.SetFloor(1, new[]
        {
            new CorpseEntry { Id = Guid.NewGuid(), PlayerId = Guid.NewGuid(), FloorNumber = 1, X = -5, Y = 0, DiedAt = DateTimeOffset.UtcNow },
            new CorpseEntry { Id = Guid.NewGuid(), PlayerId = Guid.NewGuid(), FloorNumber = 1, X = 999, Y = 999, DiedAt = DateTimeOffset.UtcNow },
            new CorpseEntry { Id = Guid.NewGuid(), PlayerId = Guid.NewGuid(), FloorNumber = 1, X = 4, Y = 4, DiedAt = DateTimeOffset.UtcNow }
        });

        var mgr = new SessionManager(TestWorld.Make(), fakeCorpses);
        var state = mgr.CreateSoloSession();
        var floor = state.GetFloor(1)!;
        var corpses = floor.Entities.Where(e => e.Type == EntityType.Corpse).ToList();

        // Two out-of-bounds rows skipped; only the (4, 4) row hydrates.
        // The exact display position may scatter (CorpsePlacement) if (4, 4)
        // is a wall in the canonical floor — the load-bearing claim is just
        // "in bounds and hydrated."
        Assert.Single(corpses);
        Assert.InRange(corpses[0].Position.X, 0, floor.Width - 1);
        Assert.InRange(corpses[0].Position.Y, 0, floor.Height - 1);
    }

    [Fact]
    public void Hydrator_carries_username_and_killer_through_to_entity()
    {
        // Step 5 needs Username + KillerType on the in-memory Entity so the
        // tooltip can render "Brac, killed by a Husk" without doing a
        // second DB lookup at hover time.
        var fakeCorpses = new FakeCorpseService();
        fakeCorpses.SetFloor(1, new[]
        {
            new CorpseEntry
            {
                Id = Guid.NewGuid(),
                PlayerId = Guid.NewGuid(),
                FloorNumber = 1,
                X = 5, Y = 5,
                DiedAt = DateTimeOffset.UtcNow,
                PlayerUsername = "Brac",
                KillerType = "Husk"
            }
        });

        var mgr = new SessionManager(TestWorld.Make(), fakeCorpses);
        var state = mgr.CreateSoloSession();
        var corpse = state.GetFloor(1)!.Entities.First(e => e.Type == EntityType.Corpse);

        Assert.Equal("Brac", corpse.Username);
        Assert.Equal("Husk", corpse.KillerType);
    }

    [Fact]
    public void Hydrator_populates_session_heatmap_for_the_floor()
    {
        // Step 9: hydrating a floor should also stash the per-tile death
        // counts on SessionState so the snapshot mapper can ship them. We
        // stub three deaths — two on (5,5), one on (6,6) — and assert the
        // resulting aggregate.
        var fakeCorpses = new FakeCorpseService();
        var pid = Guid.NewGuid();
        fakeCorpses.SetFloor(1, new[]
        {
            new CorpseEntry { Id = Guid.NewGuid(), PlayerId = pid, FloorNumber = 1, X = 5, Y = 5, DiedAt = DateTimeOffset.UtcNow.AddMinutes(-5) },
            new CorpseEntry { Id = Guid.NewGuid(), PlayerId = pid, FloorNumber = 1, X = 5, Y = 5, DiedAt = DateTimeOffset.UtcNow.AddMinutes(-4) },
            new CorpseEntry { Id = Guid.NewGuid(), PlayerId = pid, FloorNumber = 1, X = 6, Y = 6, DiedAt = DateTimeOffset.UtcNow.AddMinutes(-3) }
        });

        var mgr = new SessionManager(TestWorld.Make(), fakeCorpses);
        var state = mgr.CreateSoloSession();
        var heat = state.GetHeatmap(1);

        Assert.NotNull(heat);
        Assert.Contains(heat!, h => h.X == 5 && h.Y == 5 && h.Count == 2);
        Assert.Contains(heat, h => h.X == 6 && h.Y == 6 && h.Count == 1);
    }

    [Fact]
    public void Hydrator_carries_DiedAt_through_to_entity()
    {
        // Step 4 needs DiedAt on the in-memory Entity so the renderer can
        // age the corpse. Without this the snapshot ships diedAt=null and
        // every persistent corpse renders at full alpha.
        var fakeCorpses = new FakeCorpseService();
        var ts = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
        fakeCorpses.SetFloor(1, new[]
        {
            new CorpseEntry { Id = Guid.NewGuid(), PlayerId = Guid.NewGuid(), FloorNumber = 1, X = 5, Y = 5, DiedAt = ts }
        });

        var mgr = new SessionManager(TestWorld.Make(), fakeCorpses);
        var state = mgr.CreateSoloSession();
        var corpse = state.GetFloor(1)!.Entities.First(e => e.Type == EntityType.Corpse);

        Assert.Equal(ts, corpse.DiedAt);
    }

}
