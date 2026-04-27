namespace Crawlers.Server.Lobbies;

public enum LobbyJoinResult
{
    Success,
    NotFound,
    Full,
    AlreadyStarted,
    AlreadyInLobby
}

public record LobbyJoinOutcome(LobbyJoinResult Result, LobbyState? State);

public enum LobbyLeaveResult
{
    NotInLobby,
    MemberLeft,
    LobbyDisposed
}

public record LobbyLeaveOutcome(LobbyLeaveResult Result, LobbyState? State);

public enum LobbyStartResult
{
    Success,
    NotFound,
    NotHost,
    AlreadyStarted
}

public record LobbyStartOutcome(LobbyStartResult Result, LobbyState? State);
