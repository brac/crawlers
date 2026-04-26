namespace Crawlers.Domain.Models;

public record EntityStats
{
    public int Hp { get; init; }
    public int MaxHp { get; init; }
    public int Ac { get; init; }
    public int AttackMod { get; init; }
    public DiceRoll Damage { get; init; }
    public int InitiativeMod { get; init; }
    public int Speed { get; init; }
    public int SightRadius { get; init; }
    public int StrMod { get; init; }
    public int DexMod { get; init; }
    public int ConMod { get; init; }
}
