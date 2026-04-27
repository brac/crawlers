using Crawlers.Server.Contracts;

namespace Crawlers.Server.Hubs;

public interface ILobbyClient
{
    Task ReceiveLobbyUpdate(LobbyDto lobby);

    /// <summary>
    /// Sent to every member of a lobby when the host starts the game. The
    /// session id identifies the multi-player session that was just created
    /// by the lobby bridge — the client tears down its lobby connection,
    /// opens a /game connection, and calls JoinSession(sessionId, playerId).
    /// </summary>
    Task GameStarting(Guid sessionId);
}
