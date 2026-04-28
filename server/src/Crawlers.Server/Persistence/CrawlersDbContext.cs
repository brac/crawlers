using Microsoft.EntityFrameworkCore;

namespace Crawlers.Server.Persistence;

public class CrawlersDbContext : DbContext
{
    public CrawlersDbContext(DbContextOptions<CrawlersDbContext> options) : base(options) { }

    public DbSet<RunHistoryEntry> RunHistory => Set<RunHistoryEntry>();
    public DbSet<CorpseEntry> Corpses => Set<CorpseEntry>();
    public DbSet<PlayerRecord> Players => Set<PlayerRecord>();
    public DbSet<FloorRecord> Floors => Set<FloorRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayerRecord>(b =>
        {
            b.ToTable("players");
            b.HasKey(x => x.Id);
            // Username matches the client-side cap; trimmed and length-checked
            // server-side before any row hits this constraint.
            b.Property(x => x.Username).HasMaxLength(IdentityConstraints.UsernameMaxLength).IsRequired();
            b.Property(x => x.FirstSeenAt).IsRequired();
            b.Property(x => x.LastSeenAt).IsRequired();
            b.HasIndex(x => x.LastSeenAt);
        });

        modelBuilder.Entity<FloorRecord>(b =>
        {
            b.ToTable("floors");
            b.HasKey(x => x.Id);
            // Natural key — there's exactly one canonical floor per depth.
            // The unique index is what makes "load by floor number" cheap
            // and what guards against a mint race writing two rows.
            b.HasIndex(x => x.FloorNumber).IsUnique();
            b.Property(x => x.Tiles).HasColumnType("bytea").IsRequired();
            b.Property(x => x.RoomsJson).HasColumnType("jsonb").IsRequired();
            b.Property(x => x.GeneratedAt).IsRequired();
        });

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
            b.Property(x => x.PlayerUsername).HasMaxLength(IdentityConstraints.UsernameMaxLength);
            b.Property(x => x.KillerType).HasMaxLength(64);
            // World-scoped read pattern: "all corpses on floor N" runs every
            // time a session loads a floor, so a leading-FloorNumber index
            // is the hot path. The composite (FloorNumber, X, Y) index also
            // covers point queries the future heatmap stats will run.
            b.HasIndex(x => new { x.FloorNumber, x.X, x.Y });
            b.HasIndex(x => x.PlayerId);
        });
    }
}
