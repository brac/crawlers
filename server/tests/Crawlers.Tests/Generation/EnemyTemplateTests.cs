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
        Assert.Equal(e.Stats!.MaxHp, e.Stats.Hp); // Husk spawns at full HP
        Assert.Equal(11, e.Stats.Ac);
        Assert.Equal(0, e.Stats.InitiativeMod);
    }

    [Fact]
    public void Rasper_is_fast_and_fragile()
    {
        var husk = EnemyTemplates.Create(EnemyArchetype.Husk, new Position(2, 2), Guid.NewGuid());
        var e = EnemyTemplates.Create(EnemyArchetype.Rasper, new Position(2, 2), Guid.NewGuid());
        Assert.Equal("Rasper", e.Name);
        Assert.True(e.Stats!.Hp < husk.Stats!.Hp);    // less HP than Husk
        Assert.True(e.Stats.InitiativeMod > 0);       // higher initiative
        Assert.True(e.Stats.Ac >= 12);                // harder to hit
    }

    [Fact]
    public void Hulk_is_slow_and_tough()
    {
        var husk = EnemyTemplates.Create(EnemyArchetype.Husk, new Position(2, 2), Guid.NewGuid());
        var e = EnemyTemplates.Create(EnemyArchetype.Hulk, new Position(2, 2), Guid.NewGuid());
        Assert.Equal("Hulk", e.Name);
        Assert.True(e.Stats!.Hp > husk.Stats!.Hp);    // more HP than Husk
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

    // Step 2 — sanity-check the new variety pass: each new archetype
    // exists, has a sprite-key-compatible Name (matches a key under
    // characters/characterExtras in assets.json), and starts at full HP.
    [Theory]
    [InlineData(EnemyArchetype.Goblin, "Goblin")]
    [InlineData(EnemyArchetype.Skeleton, "Skeleton")]
    [InlineData(EnemyArchetype.MaskedOrc, "Masked Orc")]
    [InlineData(EnemyArchetype.Chort, "Chort")]
    [InlineData(EnemyArchetype.BigZombie, "Big Zombie")]
    [InlineData(EnemyArchetype.Ogre, "Ogre")]
    [InlineData(EnemyArchetype.BigDemon, "Big Demon")]
    public void Step2_archetype_spawns_with_expected_name_and_full_hp(EnemyArchetype archetype, string expectedName)
    {
        var e = EnemyTemplates.Create(archetype, new Position(0, 0), Guid.NewGuid());
        Assert.Equal(expectedName, e.Name);
        Assert.True(e.Stats!.MaxHp > 0);
        Assert.Equal(e.Stats.MaxHp, e.Stats.Hp);
        Assert.True(e.Stats.Ac > 0);
    }

    [Fact]
    public void Large_archetypes_hit_harder_than_small_ones()
    {
        var husk = EnemyTemplates.Create(EnemyArchetype.Husk, new Position(0, 0), Guid.NewGuid());
        var ogre = EnemyTemplates.Create(EnemyArchetype.Ogre, new Position(0, 0), Guid.NewGuid());
        // Locks the design intent that 32×36 large enemies are meaningfully
        // tougher than 16×16 small ones — both more HP and more damage.
        Assert.True(ogre.Stats!.MaxHp > husk.Stats!.MaxHp * 3);
        var huskExpected = husk.Stats.Damage.Count * (husk.Stats.Damage.Sides + 1) / 2.0
            + husk.Stats.Damage.Modifier;
        var ogreExpected = ogre.Stats.Damage.Count * (ogre.Stats.Damage.Sides + 1) / 2.0
            + ogre.Stats.Damage.Modifier;
        Assert.True(ogreExpected > huskExpected);
    }
}
