using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Generation;
using Xunit;

namespace Crawlers.Tests.Generation;

public class EnemyTemplateTests
{
    [Fact]
    public void Husk_has_baseline_stats()
    {
        var e = EnemyTemplates.Create(EnemyArchetype.Husk, new Position(2, 2), Guid.NewGuid());
        Assert.Equal("Husk", e.Name);
        Assert.Equal(8, e.Stats!.Hp);
        Assert.Equal(11, e.Stats.Ac);
        Assert.Equal(0, e.Stats.InitiativeMod);
    }

    [Fact]
    public void Rasper_is_fast_and_fragile()
    {
        var e = EnemyTemplates.Create(EnemyArchetype.Rasper, new Position(2, 2), Guid.NewGuid());
        Assert.Equal("Rasper", e.Name);
        Assert.True(e.Stats!.Hp < 8);                 // less HP than Husk
        Assert.True(e.Stats.InitiativeMod > 0);       // higher initiative
        Assert.True(e.Stats.Ac >= 12);                // harder to hit
    }

    [Fact]
    public void Hulk_is_slow_and_tough()
    {
        var e = EnemyTemplates.Create(EnemyArchetype.Hulk, new Position(2, 2), Guid.NewGuid());
        Assert.Equal("Hulk", e.Name);
        Assert.True(e.Stats!.Hp > 8);                 // more HP than Husk
        Assert.True(e.Stats.InitiativeMod < 0);       // lower initiative
        Assert.True(e.Stats.Damage.Sides >= 8);       // bigger damage die
    }

    [Fact]
    public void All_archetypes_share_base_entity_shape()
    {
        var floorId = Guid.NewGuid();
        foreach (var archetype in Enum.GetValues<EnemyArchetype>())
        {
            var e = EnemyTemplates.Create(archetype, new Position(1, 1), floorId);
            Assert.Equal(EntityType.Enemy, e.Type);
            Assert.Equal(EntityState.Alive, e.State);
            Assert.Equal(floorId, e.FloorId);
            Assert.NotEqual(Guid.Empty, e.Id);
            Assert.NotNull(e.Stats);
        }
    }
}
