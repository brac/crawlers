using Crawlers.Domain.Enums;

namespace Crawlers.Server.Contracts;

public record ItemDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsConsumable,
    ItemEffect Effect,
    int EffectValue
);
