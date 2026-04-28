namespace Crawlers.Domain.Enums;

public enum EntityType
{
    Enemy,
    Item,
    Npc,
    // A fallen player's body. Renders distinctly, doesn't block movement,
    // persists for the run. Future continuation phase will let teammates
    // loot a corpse for cross-run XP/inventory recovery (corpse-run mechanic).
    Corpse,
    // Content-and-Depth Step 3 — chest entity placed by the level
    // generator. Renders as a closed-chest sprite; does not block
    // movement and does not trigger engagement. The kind (Standard /
    // Empty / Mimic) is held server-side until the chest is opened
    // (Step 3.3) — clients only see "a closed chest".
    Chest
}
