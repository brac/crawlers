using Microsoft.EntityFrameworkCore;

namespace Crawlers.Server.Persistence;

public class CrawlersDbContext : DbContext
{
    public CrawlersDbContext(DbContextOptions<CrawlersDbContext> options) : base(options) { }

    public DbSet<RunHistoryEntry> RunHistory => Set<RunHistoryEntry>();
    public DbSet<CorpseEntry> Corpses => Set<CorpseEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RunHistoryEntry>(b =>
        {
            b.ToTable("run_history");
            b.HasKey(x => x.Id);
            b.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
            b.Property(x => x.CauseOfDeath).HasMaxLength(128);
            b.HasIndex(x => x.PlayerId);
            b.HasIndex(x => x.EndedAt);
        });

        modelBuilder.Entity<CorpseEntry>(b =>
        {
            b.ToTable("corpses");
            b.HasKey(x => x.Id);
            b.Property(x => x.CauseOfDeath).HasMaxLength(128);
            // Continuation phase will query "corpses on floor N at (x,y)" —
            // index for that lookup pattern up-front so the cross-run reads
            // it'll need are already cheap.
            b.HasIndex(x => new { x.FloorNumber, x.X, x.Y });
            b.HasIndex(x => x.PlayerId);
        });
    }
}
