namespace Crawlers.Server.Persistence;

/// <summary>
/// No-op fallback used when ConnectionStrings:DefaultConnection is empty.
/// Identity still works at runtime (the lobby flow keeps the UUID and
/// username in <see cref="Microsoft.AspNetCore.SignalR.HubCallerContext.Items"/>
/// and the lobby member list); we just don't persist the identity row.
/// </summary>
public class NullPlayerIdentityService : IPlayerIdentityService
{
    private readonly ILogger<NullPlayerIdentityService> _logger;

    public NullPlayerIdentityService(ILogger<NullPlayerIdentityService> logger) => _logger = logger;

    public Task IdentifyAsync(Guid playerId, string username, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Player identity persistence disabled — Identify({PlayerId}, '{Username}') is a no-op.",
            playerId, username);
        return Task.CompletedTask;
    }

    public Task<string?> GetUsernameAsync(Guid playerId, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
