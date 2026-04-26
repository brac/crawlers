using Crawlers.Domain.Models;

namespace Crawlers.Server.Logic;

/// <summary>
/// Thin wrapper over Random for combat rolls. Pass a seeded instance from tests
/// for deterministic outcomes; default ctor uses time-seeded randomness.
/// Not thread-safe; combat lookups acquire SessionState.SyncRoot before use.
/// </summary>
public class Dice
{
    private readonly Random _rng;

    public Dice() : this(new Random()) { }
    public Dice(int seed) : this(new Random(seed)) { }
    public Dice(Random rng) => _rng = rng;

    public virtual int D20() => _rng.Next(1, 21);

    public virtual int Roll(DiceRoll d)
    {
        int sum = 0;
        for (int i = 0; i < d.Count; i++)
            sum += _rng.Next(1, d.Sides + 1);
        return sum + d.Modifier;
    }

    /// <summary>Roll the dice without applying the modifier (used for crits where dice are doubled but mod isn't).</summary>
    public virtual int RollDiceOnly(DiceRoll d)
    {
        int sum = 0;
        for (int i = 0; i < d.Count; i++)
            sum += _rng.Next(1, d.Sides + 1);
        return sum;
    }
}
