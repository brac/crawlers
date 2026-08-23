using System.Security.Cryptography;
using System.Text;

namespace Crawlers.Server.Security;

/// <summary>
/// Mints and verifies the per-player session token that binds a /game
/// connection to a player.
///
/// Why this exists: a player id cannot be a credential. Every player id in a
/// session is published to every other client (the lobby roster carries them,
/// and so does the OtherPlayers list in every snapshot), so anything that
/// accepts a bare player id as proof of identity accepts it from anyone who
/// has ever shared a lobby with that player. The token is the secret half:
/// minted server-side, handed back only to the connection that created or
/// joined the lobby seat, and never placed in any payload that fans out to
/// more than one client.
///
/// The token is a bearer credential for one player in one session and nothing
/// else. It is not an account password, it is not reused across sessions, and
/// it dies with the session.
/// </summary>
public static class SessionTokens
{
    /// <summary>
    /// 256 bits of entropy. Well past the 128-bit floor, and the base64url
    /// encoding of it is still short enough to sit comfortably in a hub
    /// argument.
    /// </summary>
    public const int TokenByteLength = 32;

    /// <summary>
    /// Mint a fresh token from the OS CSPRNG. Deliberately takes no
    /// arguments: a token derived from the player id, the username, the
    /// session id, or anything else the client supplies would be forgeable by
    /// whoever supplied it.
    /// </summary>
    public static string Mint()
    {
        var bytes = new byte[TokenByteLength];
        RandomNumberGenerator.Fill(bytes);
        return ToBase64Url(bytes);
    }

    /// <summary>
    /// Constant-time comparison of a stored token against a presented one.
    /// Uses <see cref="CryptographicOperations.FixedTimeEquals"/> over the
    /// UTF-8 bytes rather than string equality so the compare does not leak
    /// a matching prefix through its running time. Length mismatch returns
    /// false (FixedTimeEquals's own short-circuit); that leaks only the
    /// length of a random constant-length token, which tells an attacker
    /// nothing.
    /// </summary>
    public static bool Matches(string? stored, string? presented)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(presented)) return false;

        var a = Encoding.UTF8.GetBytes(stored);
        var b = Encoding.UTF8.GetBytes(presented);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Unpadded base64url (RFC 4648 section 5) so the token is safe to put in
    /// a JSON body, a header, or storage without escaping. It is never put in
    /// a URL or query string, but encoding it URL-safely costs nothing and
    /// removes a whole class of future footgun.
    /// </summary>
    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
