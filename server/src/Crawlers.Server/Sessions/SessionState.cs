using Crawlers.Domain.Models;

namespace Crawlers.Server.Sessions;

public class SessionState
{
    public Session Session { get; }
    public Floor Floor { get; private set; }
    public Player Player { get; }
    public ActiveCombat? ActiveCombat { get; set; }

    /// <summary>Total enemies killed by this session over its lifetime.</summary>
    public int EnemiesKilled { get; set; }

    /// <summary>
    /// Seed used to generate floor 1; subsequent floors derive their seeds
    /// deterministically from this so an entire run can be replayed.
    /// </summary>
    public int InitialSeed { get; init; }

    public void ReplaceFloor(Floor newFloor) => Floor = newFloor;

    /// <summary>
    /// Lock object held during any mutation of this session — by hub methods
    /// (Move, Flee) and by the CombatRunner background tick loop. Acquire
    /// before reading or writing anything on Session/Player/Floor/ActiveCombat.
    /// </summary>
    public object SyncRoot { get; } = new();

    public SessionState(Session session, Floor floor, Player player)
    {
        Session = session;
        Floor = floor;
        Player = player;
    }
}
