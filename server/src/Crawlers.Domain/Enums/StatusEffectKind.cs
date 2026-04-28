namespace Crawlers.Domain.Enums;

/// <summary>
/// Step 5 — lightweight status effects. Bleed ticks at the start of
/// the victim's turn; Poison ticks at the end. Antidote clears both.
/// More effects (Burn / Slow / Stun / …) can append here later.
/// </summary>
public enum StatusEffectKind
{
    Bleed = 0,
    Poison = 1
}
