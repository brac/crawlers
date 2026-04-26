using Crawlers.Domain.Enums;

namespace Crawlers.Server.Contracts;

public record EntityDto(
    Guid Id,
    EntityType Type,
    string Name,
    int X,
    int Y
);
