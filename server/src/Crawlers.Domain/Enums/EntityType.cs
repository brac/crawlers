namespace Crawlers.Domain.Enums;

public enum EntityType
{
    Enemy,
    Item,
    Npc,
    // A fallen player's body. Renders distinctly, doesn't block movement,
    // persists for the run. Future continuation phase will let teammates
    // loot a corpse for cross-run XP/inventory recovery (corpse-run mechanic).
    Corpse
}
