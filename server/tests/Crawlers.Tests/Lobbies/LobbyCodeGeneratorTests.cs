using Crawlers.Server.Lobbies;

namespace Crawlers.Tests.Lobbies;

public class LobbyCodeGeneratorTests
{
    [Fact]
    public void Generate_returns_six_characters_from_the_unambiguous_alphabet()
    {
        var rng = new Random(1);
        for (int i = 0; i < 50; i++)
        {
            var code = LobbyCodeGenerator.Generate(rng);
            Assert.Equal(LobbyCodeGenerator.CodeLength, code.Length);
            foreach (var c in code)
                Assert.Contains(c, LobbyCodeGenerator.Alphabet);
        }
    }

    [Fact]
    public void Alphabet_excludes_visually_ambiguous_glyphs()
    {
        // Confused-with-zero/one cluster: 0, 1, I, O, L.
        Assert.DoesNotContain('0', LobbyCodeGenerator.Alphabet);
        Assert.DoesNotContain('1', LobbyCodeGenerator.Alphabet);
        Assert.DoesNotContain('I', LobbyCodeGenerator.Alphabet);
        Assert.DoesNotContain('O', LobbyCodeGenerator.Alphabet);
        Assert.DoesNotContain('L', LobbyCodeGenerator.Alphabet);
    }

    [Fact]
    public void Generate_with_same_seed_is_deterministic()
    {
        var a = LobbyCodeGenerator.Generate(new Random(42));
        var b = LobbyCodeGenerator.Generate(new Random(42));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Normalize_uppercases_and_trims()
    {
        Assert.Equal("ABCDEF", LobbyCodeGenerator.Normalize("  abcdef  "));
        Assert.Equal("XYZ234", LobbyCodeGenerator.Normalize("xYz234"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABC")]                // too short
    [InlineData("ABCDEFG")]            // too long
    [InlineData("ABCDE0")]             // contains forbidden 0
    [InlineData("ABCDE1")]             // contains forbidden 1
    [InlineData("ABCDEI")]             // contains forbidden I
    [InlineData("ABCDEO")]             // contains forbidden O
    [InlineData("ABCDEL")]             // contains forbidden L
    [InlineData("ABCDE-")]             // non-alphanumeric
    public void Normalize_returns_null_for_invalid_input(string? input)
    {
        Assert.Null(LobbyCodeGenerator.Normalize(input));
    }
}
