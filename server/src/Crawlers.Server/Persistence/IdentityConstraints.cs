namespace Crawlers.Server.Persistence;

/// <summary>
/// Constants shared by the database column constraint and the hub-level
/// validator. Keeping them in one place stops the EF column max length and
/// the LobbyHub.Identify guard from drifting.
/// </summary>
public static class IdentityConstraints
{
    public const int UsernameMaxLength = 24;
    public const int UsernameMinLength = 1;
}
