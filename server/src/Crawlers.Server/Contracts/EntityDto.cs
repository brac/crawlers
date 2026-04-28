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
    string? KillerType,
    // Content-and-Depth Step 3 — chest kind (Standard / Empty / Mimic).
    // Set only when Type == Chest. The client uses this to pick the
    // right sprite per kind. (Production rule that mimics should be
    // visually indistinguishable from Standard chests is tracked
    // separately and will be enforced in Step 3.3.)
    ChestKind? ChestKind,
    // Content-and-Depth Step 3.3 — chest open/closed state. False on
    // non-chest entities and on freshly-placed (still-closed) chests;
    // flips true the first time a player opens this chest. Drives the
    // open-sprite swap on the client.
    bool IsOpen
);
