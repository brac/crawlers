using Crawlers.Domain.Enums;
using Crawlers.Domain.Models;

namespace Crawlers.Server.Contracts;

public record ItemDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsConsumable,
    ItemEffect Effect,
    int EffectValue,
    // Step 3.4 — set when the item is a weapon. Renderer uses
    // assets.weaponTexture(Name) for the icon and the inventory panel
    // surfaces damage / init mod. Equip mechanics ship in Step 3.5.
    WeaponBlock? Weapon
);
