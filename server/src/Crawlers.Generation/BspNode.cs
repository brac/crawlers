using Crawlers.Domain.Models;

namespace Crawlers.Generation;

internal class BspNode
{
    public Bounds Bounds { get; }
    public BspNode? Left { get; private set; }
    public BspNode? Right { get; private set; }
    public Room? Room { get; set; }

    public bool IsLeaf => Left is null && Right is null;

    public BspNode(Bounds bounds)
    {
        Bounds = bounds;
    }

    public IEnumerable<BspNode> Leaves()
    {
        if (IsLeaf)
        {
            yield return this;
            yield break;
        }
        if (Left is not null)
            foreach (var l in Left.Leaves()) yield return l;
        if (Right is not null)
            foreach (var l in Right.Leaves()) yield return l;
    }

    public bool TrySplit(Random rng, int minPartitionSize)
    {
        bool canSplitVertical = Bounds.Width >= minPartitionSize * 2;
        bool canSplitHorizontal = Bounds.Height >= minPartitionSize * 2;

        if (!canSplitVertical && !canSplitHorizontal) return false;

        bool splitVertical;
        double aspectRatio = (double)Bounds.Width / Bounds.Height;
        if (aspectRatio > 1.25 && canSplitVertical) splitVertical = true;
        else if (aspectRatio < 0.8 && canSplitHorizontal) splitVertical = false;
        else if (canSplitVertical && canSplitHorizontal) splitVertical = rng.Next(2) == 0;
        else splitVertical = canSplitVertical;

        if (splitVertical)
        {
            int splitX = rng.Next(minPartitionSize, Bounds.Width - minPartitionSize + 1);
            Left = new BspNode(new Bounds(Bounds.X, Bounds.Y, splitX, Bounds.Height));
            Right = new BspNode(new Bounds(Bounds.X + splitX, Bounds.Y, Bounds.Width - splitX, Bounds.Height));
        }
        else
        {
            int splitY = rng.Next(minPartitionSize, Bounds.Height - minPartitionSize + 1);
            Left = new BspNode(new Bounds(Bounds.X, Bounds.Y, Bounds.Width, splitY));
            Right = new BspNode(new Bounds(Bounds.X, Bounds.Y + splitY, Bounds.Width, Bounds.Height - splitY));
        }
        return true;
    }
}
