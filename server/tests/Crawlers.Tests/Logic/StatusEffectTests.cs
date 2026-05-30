using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;
using Crawlers.Server.Logic;
using Xunit;

namespace Crawlers.Tests.Logic;

/// <summary>
/// Step 5 — locks the StatusEffectHelper contracts (stacking refresh,
/// decrement, tick damage) and the antidote-consumable behavior.
/// Combat tick wiring is exercised via integration tests in
/// <see cref="StatusEffectInCombatTests"/>.
/// </summary>
public class StatusEffectTests
{
    [Fact]
    public void Apply_adds_new_effect()
    {
        var list = new List<StatusEffect>();
        StatusEffectHelper.Apply(list, new StatusEffect(StatusEffectKind.Bleed, 3, 2));
        Assert.Single(list);
        Assert.Equal(StatusEffectKind.Bleed, list[0].Kind);
        Assert.Equal(3, list[0].RoundsRemaining);
        Assert.Equal(2, list[0].DamagePerTick);
    }

    [Fact]
    public void Apply_same_kind_refreshes_to_longer_duration_and_higher_damage()
    {
        var list = new List<StatusEffect>
        {
            new(StatusEffectKind.Bleed, RoundsRemaining: 1, DamagePerTick: 3)
        };
        // Incoming has more rounds but lower damage — keep both maxes.
        StatusEffectHelper.Apply(list, new StatusEffect(StatusEffectKind.Bleed, 4, 2));
        Assert.Single(list);
        Assert.Equal(4, list[0].RoundsRemaining);
        Assert.Equal(3, list[0].DamagePerTick);
    }

    [Fact]
    public void Apply_different_kind_coexists()
    {
        var list = new List<StatusEffect> { new(StatusEffectKind.Bleed, 2, 1) };
        StatusEffectHelper.Apply(list, new StatusEffect(StatusEffectKind.Poison, 3, 2));
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Decrement_removes_zero_round_effects()
    {
        var list = new List<StatusEffect>
        {
            new(StatusEffectKind.Bleed, 1, 2),
            new(StatusEffectKind.Poison, 3, 1)
        };
        StatusEffectHelper.Decrement(list);
        Assert.Single(list);
        Assert.Equal(StatusEffectKind.Poison, list[0].Kind);
        Assert.Equal(2, list[0].RoundsRemaining);
    }

    [Fact]
    public void TickDamage_sums_per_kind_only()
    {
        var list = new List<StatusEffect>
        {
            new(StatusEffectKind.Bleed, 2, 2),
            new(StatusEffectKind.Poison, 3, 1)
        };
        Assert.Equal(2, StatusEffectHelper.TickDamage(list, StatusEffectKind.Bleed));
        Assert.Equal(1, StatusEffectHelper.TickDamage(list, StatusEffectKind.Poison));
    }

    [Fact]
    public void Antidote_clears_all_status_effects()
    {
        var (state, _) = CombatTestFactory.BuildEngagement();
        var p = state.PrimaryPlayer;
        p.StatusEffects.Add(new StatusEffect(StatusEffectKind.Bleed, 3, 2));
        p.StatusEffects.Add(new StatusEffect(StatusEffectKind.Poison, 4, 1));

        ItemUseHelper.Apply(p, ItemTemplates.Antidote());

        Assert.Empty(p.StatusEffects);
    }

    [Fact]
    public void Antidote_on_unaffected_player_is_a_no_op_message()
    {
        var (state, _) = CombatTestFactory.BuildEngagement();
        var p = state.PrimaryPlayer;
        var msg = ItemUseHelper.Apply(p, ItemTemplates.Antidote());
        Assert.NotNull(msg);
        Assert.Contains("no different", msg);
    }

    [Fact]
    public void Statuses_tick_per_move_in_exploration()
    {
        // Step 5.G — the gap the user flagged: after combat ends with
        // statuses still on the player, the next move should tick them
        // (drain HP + decrement) instead of leaving them frozen.
        var state = CombatTestFactory.BuildExploration(playerAt: new Position(2, 2));
        var p = state.PrimaryPlayer;
        var hp0 = p.Stats.Hp;
        p.StatusEffects.Add(new StatusEffect(StatusEffectKind.Bleed, 3, 2));
        p.StatusEffects.Add(new StatusEffect(StatusEffectKind.Poison, 3, 1));

        new MovementService().TryMove(state, p.Id, MoveDirection.East);

        // First move: 2 bleed + 1 poison = 3 damage. Both effects
        // decrement to 2 rounds remaining.
        Assert.Equal(hp0 - 3, p.Stats.Hp);
        Assert.Equal(2, p.StatusEffects.Single(s => s.Kind == StatusEffectKind.Bleed).RoundsRemaining);
        Assert.Equal(2, p.StatusEffects.Single(s => s.Kind == StatusEffectKind.Poison).RoundsRemaining);
    }

    [Fact]
    public void Status_tick_naturally_expires_after_remaining_rounds()
    {
        var state = CombatTestFactory.BuildExploration(playerAt: new Position(2, 2));
        var p = state.PrimaryPlayer;
        p.StatusEffects.Add(new StatusEffect(StatusEffectKind.Bleed, 2, 1));

        new MovementService().TryMove(state, p.Id, MoveDirection.East);
        new MovementService().TryMove(state, p.Id, MoveDirection.East);

        // After 2 moves, Bleed has ticked twice and decremented to 0 → removed.
        Assert.Empty(p.StatusEffects);
    }

    [Fact]
    public void Status_tick_can_kill_player_outside_combat()
    {
        // Lethal poison tick on a low-HP player should mark them dead
        // and drop a corpse, mirroring the combat death sequence.
        var state = CombatTestFactory.BuildExploration(playerAt: new Position(2, 2));
        var p = state.PrimaryPlayer;
        p.Stats = p.Stats with { Hp = 1 };
        p.StatusEffects.Add(new StatusEffect(StatusEffectKind.Bleed, 2, 5));

        new MovementService().TryMove(state, p.Id, MoveDirection.East);

        Assert.Equal(GameMode.Resolution, p.Mode);
        Assert.NotNull(p.DiedAt);
        Assert.Equal("Bled out", p.CauseOfDeath);
        var floor = state.GetFloorFor(p);
        Assert.Contains(floor.Entities, e => e.Type == EntityType.Corpse && e.PlayerId == p.Id);
    }

    [Fact]
    public void Poison_kill_in_combat_marks_dead_and_skips_post_death_decrement()
    {
        // Regression: a lethal end-of-turn poison tick kills the player and the
        // turn short-circuits, so their lingering effects are NOT decremented a
        // final time on a corpse. (The buggy path ran Decrement after death.)
        var (state, enemy) = CombatTestFactory.BuildEngagement(playerHp: 1, enemyHp: 50);
        var p = state.PrimaryPlayer;
        p.StatusEffects.Add(new StatusEffect(StatusEffectKind.Poison, RoundsRemaining: 2, DamagePerTick: 5));

        // Initiative: player (18) before enemy (3). Player's attack misses
        // (roll 3 → no damage dice consumed); the end-of-turn poison tick then
        // drops them from 1 HP to 0 before the enemy ever acts.
        var dice = new ScriptedDice(d20: new[] { 18, 3, 3 });

        var svc = new CombatService();
        var combat = svc.Start(state, enemy, new[] { p }, dice);
        var result = svc.ProcessNextRound(state, combat, dice);

        Assert.True(result.Ended);
        Assert.Equal(GameMode.Resolution, p.Mode);
        Assert.DoesNotContain(p.Id, combat.ParticipantPlayerIds);
        Assert.Equal(2, p.StatusEffects.Single(s => s.Kind == StatusEffectKind.Poison).RoundsRemaining);
    }
}
