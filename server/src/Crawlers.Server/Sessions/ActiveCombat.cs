using Crawlers.Domain.Models;

namespace Crawlers.Server.Sessions;

public class ActiveCombat
{
    public Guid EnemyId { get; init; }
    public bool PlayerActsFirst { get; init; }
    public int RoundNumber { get; set; }
    public bool FleeRequested { get; set; }
    public Guid? UseItemRequested { get; set; }
    public CombatLog Log { get; init; } = new();
}
