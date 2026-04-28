using Crawlers.Server.Logic;
using Xunit;

namespace Crawlers.Tests.Logic;

/// <summary>
/// Step 9 — locks the cycle-aware naming so endless descent reads
/// narratively. Names themselves are placeholders by design (the
/// design doc says "we'll spice them up later"); the contract here
/// is just that each floor has a stable, distinct, non-empty name.
/// </summary>
public class FloorNameResolverTests
{
    [Theory]
    [InlineData(1, "The Crypt")]
    [InlineData(2, "The Sewers")]
    [InlineData(3, "The Caverns")]
    [InlineData(4, "The Hellscape")]
    public void Cycle0_returns_base_name(int floor, string expected)
    {
        Assert.Equal(expected, FloorNameResolver.For(floor));
    }

    [Theory]
    [InlineData(5, "Deeper Crypt")]
    [InlineData(6, "Deeper Sewers")]
    [InlineData(8, "Deeper Hellscape")]
    public void Cycle1_uses_deeper_prefix(int floor, string expected)
    {
        Assert.Equal(expected, FloorNameResolver.For(floor));
    }

    [Theory]
    [InlineData(9, "Forgotten Crypt")]
    [InlineData(13, "Sunken Crypt")]
    public void Later_cycles_walk_through_the_prefix_table(int floor, string expected)
    {
        Assert.Equal(expected, FloorNameResolver.For(floor));
    }

    [Fact]
    public void Sub_one_floors_clamp_to_floor_one()
    {
        Assert.Equal("The Crypt", FloorNameResolver.For(0));
        Assert.Equal("The Crypt", FloorNameResolver.For(-5));
    }

    [Fact]
    public void Names_never_double_up_The_in_prefixed_form()
    {
        // "Deeper The Crypt" would read poorly — we strip the leading
        // "The " from the base name before applying the prefix.
        var name = FloorNameResolver.For(5);
        Assert.DoesNotContain("Deeper The", name);
    }
}
