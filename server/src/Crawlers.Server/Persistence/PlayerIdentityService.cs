using Microsoft.EntityFrameworkCore;

namespace Crawlers.Server.Persistence;

/// <summary>
/// Postgres-backed implementation. Singleton lifetime — uses
/// <see cref="IServiceScopeFactory"/> to grab a per-call DbContext so the
/// scoped EF provider stays correctly scoped.
/// </summary>
public class PlayerIdentityService : IPlayerIdentityService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlayerIdentityService> _logger;

    public PlayerIdentityService(IServiceScopeFactory scopeFactory, ILogger<PlayerIdentityService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task IdentifyAsync(Guid playerId, string username, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrawlersDbContext>();

        var now = DateTimeOffset.UtcNow;
        var row = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, ct);
        if (row is null)
        {
            db.Players.Add(new PlayerRecord
            {
                Id = playerId,
                Username = username,
                FirstSeenAt = now,
                LastSeenAt = now
            });
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Registered new player identity {PlayerId} as '{Username}'",
                playerId, username);
            return;
        }

        if (!string.Equals(row.Username, username, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Player {PlayerId} renamed '{Old}' → '{New}'",
                playerId, row.Username, username);
        }

        row.Username = username;
        row.LastSeenAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> GetUsernameAsync(Guid playerId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrawlersDbContext>();
        return await db.Players
            .Where(p => p.Id == playerId)
            .Select(p => p.Username)
            .FirstOrDefaultAsync(ct);
    }
}
