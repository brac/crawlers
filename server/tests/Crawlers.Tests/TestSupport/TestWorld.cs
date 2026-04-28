using System.Collections.Generic;
using System.Globalization;
using Crawlers.Server.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crawlers.Tests.TestSupport;

/// <summary>
/// Shared test factory for the in-memory <see cref="IFloorWorldService"/>.
/// Step 2 of the persistent-world phase made <see cref="Crawlers.Server.Sessions.SessionManager"/>
/// and <see cref="Crawlers.Server.Logic.DescendService"/> require a world
/// service; this is the lightweight stand-in tests use.
/// </summary>
internal static class TestWorld
{
    /// <summary>
    /// Build a fresh in-memory world with the given base seed. Tests that
    /// care about deterministic floor geometry pass an explicit seed;
    /// everyone else accepts the default.
    /// </summary>
    public static IFloorWorldService Make(int baseSeed = 0x1afe5c3)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["World:BaseSeed"] = baseSeed.ToString(CultureInfo.InvariantCulture)
            })
            .Build();
        return new NullFloorWorldService(NullLogger<NullFloorWorldService>.Instance, config);
    }

    /// <summary>
    /// Test-only corpse service: no-op writes, empty floor reads. Step 3 of
    /// the persistent-world phase made <see cref="Crawlers.Server.Sessions.SessionManager"/>
    /// and <see cref="Crawlers.Server.Logic.DescendService"/> require this.
    /// </summary>
    public static ICorpseService MakeCorpses()
        => new NullCorpseService(NullLogger<NullCorpseService>.Instance);
}
