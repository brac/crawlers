namespace Crawlers.Domain.Enums;

/// <summary>
/// Catalogue of enemy archetypes. The numeric value is stable wire format
/// for the entity contract — append-only; do not renumber existing
/// entries. The string name doubles as the JSON key in
/// <c>floor-scaling.json</c>'s monster pools.
/// </summary>
public enum EnemyArchetype
{
    // Step 1 set — kept for tutorial floor 1 and the original 3-archetype mix.
    Husk = 0,
    Rasper = 1,
    Hulk = 2,
    TinySlug = 3,
    BigSlug = 4,
    // Content-and-Depth Step 2 — variety pass. Sprites already declared in
    // client/public/assets/dungeon/assets.json; per-floor pools in
    // floor-scaling.json decide which floors they're eligible to spawn on.
    Goblin = 5,
    Skeleton = 6,
    MaskedOrc = 7,
    Chort = 8,
    BigZombie = 9,
    Ogre = 10,
    BigDemon = 11,
    // Content-and-Depth Step 3.6 — mimic enemy. Spawned by
    // ChestService when a Mimic chest is opened (the chest entity is
    // removed and replaced by this enemy at the chest's tile). Visual
    // is a chomping chest using the manifest's three mimic chest
    // frames as a 3-frame idle/run loop.
    Mimic = 12
}
