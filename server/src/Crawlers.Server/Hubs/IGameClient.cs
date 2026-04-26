using Crawlers.Server.Contracts;

namespace Crawlers.Server.Hubs;

public interface IGameClient
{
    Task ReceiveSnapshot(GameStateSnapshotDto snapshot);
}
