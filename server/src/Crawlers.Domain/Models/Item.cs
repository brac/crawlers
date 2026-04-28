using Crawlers.Domain.Enums;

namespace Crawlers.Domain.Models;

public record Item
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsConsumable { get; init; }
    public ItemEffect Effect { get; init; } = ItemEffect.None;
    public int EffectValue { get; init; }

    /// <summary>
    /// Content-and-Depth Step 3.4 — weapon stat block. Set when the item
    /// is a weapon archetype (drawn from a chest's loot pool); null on
    /// every consumable / non-weapon. Step 3.5 will use these values to
    /// override the player's combat dice + initiative when the weapon
    /// is equipped. <see cref="Item.Name"/> doubles as the manifest
    /// sprite key on the client (assets.json &gt; weapons).
    /// </summary>
    public WeaponBlock? Weapon { get; init; }
}

public sealed record WeaponBlock(DiceRoll Damage, int InitiativeMod);
