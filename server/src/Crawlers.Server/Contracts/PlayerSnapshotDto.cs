using Crawlers.Domain.Models;

namespace Crawlers.Server.Contracts;

public record PlayerSnapshotDto(
    Guid Id,
    string Username,
    int X,
    int Y,
    int Hp,
    int MaxHp,
    IReadOnlyList<ItemDto> Inventory,
    // Step 3.4 — equipped weapon (drives combat damage + initiative).
    // Null only on legacy snapshots before this field existed; fresh
    // sessions always get a Regular Sword by default.
    WeaponBlock? EquippedWeapon,
    // Step 3.4 — gold counter. Standard chests credit it; purely
    // accumulative for now (no sink).
    int Gold,
    // Step 3.4 — name of the weapon currently equipped (matches client
    // manifest key under weapons). Surfaced separately from
    // EquippedWeapon so the HUD doesn't have to reverse-look-up names
    // from stat blocks.
    string? EquippedWeaponName,
    // Step 5 — active Bleed / Poison status effects on the local
    // player. HUD renders one badge per entry next to the HP bar.
    IReadOnlyList<StatusEffect> StatusEffects
);
