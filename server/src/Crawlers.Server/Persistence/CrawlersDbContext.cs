using Microsoft.EntityFrameworkCore;

namespace Crawlers.Server.Persistence;

public class CrawlersDbContext : DbContext
{
    public CrawlersDbContext(DbContextOptions<CrawlersDbContext> options) : base(options) { }

    public DbSet<RunHistoryEntry> RunHistory => Set<RunHistoryEntry>();

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
    }
}
