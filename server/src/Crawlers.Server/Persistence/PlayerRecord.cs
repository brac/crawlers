namespace Crawlers.Server.Persistence;

/// <summary>
/// Persistent identity row keyed by the UUID the browser stores in
/// localStorage. One row per identity that has ever connected. Username is
/// rewritten on every <see cref="IPlayerIdentityService.IdentifyAsync"/> call
/// so a returning player can change their display name without losing their
/// id (and therefore their corpse history).
/// </summary>
public class PlayerRecord
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}
