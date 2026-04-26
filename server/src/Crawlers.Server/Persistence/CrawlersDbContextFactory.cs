using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Crawlers.Server.Persistence;

/// <summary>
/// Used only by `dotnet ef` design-time tooling (migrations, scaffolding).
/// At runtime the DI container builds a DbContext from configured services
/// — this factory is never invoked there.
/// </summary>
public class CrawlersDbContextFactory : IDesignTimeDbContextFactory<CrawlersDbContext>
{
    public CrawlersDbContext CreateDbContext(string[] args)
    {
        // Connection string only needs to parse; migrations generation does
        // not actually open a connection to Postgres.
        var options = new DbContextOptionsBuilder<CrawlersDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=crawlers_design;Username=design;Password=design")
            .Options;
        return new CrawlersDbContext(options);
    }
}
