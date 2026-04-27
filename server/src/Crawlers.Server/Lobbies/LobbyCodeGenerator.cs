namespace Crawlers.Server.Lobbies;

/// <summary>
/// Produces 6-character lobby codes from a 32-character alphabet that omits
/// visually ambiguous glyphs (0/O, 1/I/L) so a code read aloud or off a screen
/// transcribes unambiguously.
/// </summary>
public static class LobbyCodeGenerator
{
    public const int CodeLength = 6;
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string Generate(Random rng)
    {
        Span<char> buffer = stackalloc char[CodeLength];
        for (int i = 0; i < CodeLength; i++)
            buffer[i] = Alphabet[rng.Next(Alphabet.Length)];
        return new string(buffer);
    }

    /// <summary>
    /// Returns the canonical (uppercase, trimmed) form of a code, or null if the
    /// input is not a syntactically valid code in this alphabet.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var trimmed = input.Trim().ToUpperInvariant();
        if (trimmed.Length != CodeLength) return null;
        foreach (var c in trimmed)
            if (Alphabet.IndexOf(c) < 0) return null;
        return trimmed;
    }
}
