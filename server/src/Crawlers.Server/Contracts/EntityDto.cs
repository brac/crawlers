using Crawlers.Domain.Enums;

namespace Crawlers.Server.Contracts;

public record EntityDto(
    Guid Id,
    EntityType Type,
    string Name,
    int X,
    int Y,
    // Set on Corpse entities — drives Step 4 visual aging on the client.
    // Null for entities the renderer doesn't fade (enemies, items).
    DateTimeOffset? DiedAt,
    // Step 5 tooltip metadata — set on Corpse entities only. Username
    // freezes at the moment of death so renames don't rewrite headstones;
    // KillerType is the short archetype tag ("Husk", "TinySlug", …) or
    // null for non-combat deaths and legacy rows.
    string? Username,
    string? KillerType
);
