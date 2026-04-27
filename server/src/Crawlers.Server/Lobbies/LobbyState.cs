using Crawlers.Domain.Models;

namespace Crawlers.Server.Lobbies;

public class LobbyState
{
    public LobbyRoom Room { get; }

    /// <summary>
    /// Set to true under <see cref="SyncRoot"/> when the manager removes this
    /// lobby from its dictionaries. Joiners that captured the state reference
    /// before the lock check this flag and back out, avoiding adding a member
    /// to a lobby that's already been disposed.
    /// </summary>
    public bool Disposed { get; set; }

    /// <summary>
    /// Lock object held during any mutation of this lobby. Acquire before
    /// reading or writing anything on Room.Members / Room.Status / Room.HostPlayerId.
    /// </summary>
    public object SyncRoot { get; } = new();

    public LobbyState(LobbyRoom room)
    {
        Room = room;
    }
}
