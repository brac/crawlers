namespace Crawlers.Server.Persistence;

/// <summary>
/// One entry in a floor's death heatmap (Step 9): the tile coordinate and
/// how many corpses ever fell on it across every session in the world's
/// history. Derived view of the <c>corpses</c> table; never stored
/// directly. Sparse — only tiles meeting the death threshold are emitted.
/// </summary>
public record TileHeat(int X, int Y, int Count);
