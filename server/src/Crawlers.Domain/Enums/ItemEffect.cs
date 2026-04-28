namespace Crawlers.Domain.Enums;

public enum ItemEffect
{
    None = 0,
    Heal = 1,
    /// <summary>Passive bonus while item is in inventory (e.g. Bone Charm).</summary>
    AttackBonus = 2,
    /// <summary>Passive bonus while item is in inventory.</summary>
    DefenseBonus = 3,
    // Step 4 — consumable effects that mutate Stats permanently for the
    // run when used. The value is the integer to add. The item is
    // consumed (removed from inventory) by the caller, mirroring how
    // Heal already works.
    AttackBonusPermanent = 4,
    InitiativeBonusPermanent = 5,
    /// <summary>Step 5 — clears all StatusEffects on the user (Antidote).</summary>
    CureStatuses = 6
}
