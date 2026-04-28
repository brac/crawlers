namespace Crawlers.Server.Persistence;

/// <summary>
/// Upsert + lookup for the persistent identity table. Username comes in pre-
/// validated (trimmed, length-checked) — implementations only store and
/// retrieve. The Postgres impl scopes a fresh DbContext per call so it can be
/// safely registered as a singleton next to the long-lived hub services.
/// </summary>
public interface IPlayerIdentityService
{
    /// <summary>
    /// Insert the row if this UUID is new, or update the username +
    /// last-seen-at if it already exists. Idempotent — same call twice is
    /// equivalent to one call.
    /// </summary>
    Task IdentifyAsync(Guid playerId, string username, CancellationToken ct = default);

    /// <summary>
    /// Returns the persisted username for this id, or null if no row exists.
    /// Used by reconnect / session-rejoin paths that need to refresh the
    /// in-memory <see cref="Crawlers.Domain.Models.Player.Username"/> from
    /// the source of truth.
    /// </summary>
    Task<string?> GetUsernameAsync(Guid playerId, CancellationToken ct = default);
}
