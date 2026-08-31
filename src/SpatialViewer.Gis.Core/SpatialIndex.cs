namespace SpatialViewer.Gis.Core;

public readonly record struct SpatialIndexEntry<T>(Envelope2D Bounds, T Value);

/// <summary>
/// Immutable packed R-tree for envelope intersection queries.
/// The tree stores lightweight bounds and caller-owned values, not feature geometry payloads.
/// </summary>
public sealed class PackedRTree<T>
{
    private const int DefaultNodeCapacity = 16;
    private readonly Node? _root;

    private PackedRTree(Node? root, int count)
    {
        _root = root;
        Count = count;
    }

    public int Count { get; }

    public static PackedRTree<T> Build(
        IReadOnlyList<SpatialIndexEntry<T>> entries,
        int nodeCapacity = DefaultNodeCapacity)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (nodeCapacity < 4)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nodeCapacity),
                nodeCapacity,
                "Packed R-tree node capacity must be at least four.");
        }

        if (entries.Count == 0)
        {
            return new PackedRTree<T>(null, 0);
        }

        var leafEntries = new SpatialIndexEntry<T>[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (!entry.Bounds.IsValid)
            {
                throw new ArgumentException(
                    $"Spatial index entry {index} has invalid bounds.",
                    nameof(entries));
            }

            leafEntries[index] = entry;
        }

        Array.Sort(leafEntries, static (left, right) => CompareBounds(left.Bounds, right.Bounds));
        var nodes = BuildLeafLevel(leafEntries, nodeCapacity);

        while (nodes.Length > 1)
        {
            nodes = BuildParentLevel(nodes, nodeCapacity);
        }

        return new PackedRTree<T>(nodes[0], entries.Count);
    }

    public IReadOnlyList<T> Query(Envelope2D extent)
    {
        if (!extent.IsValid)
        {
            throw new ArgumentException("Spatial index query extent must be valid.", nameof(extent));
        }

        if (_root is null)
        {
            return Array.Empty<T>();
        }

        var result = new List<T>();
        var pending = new Stack<Node>();
        pending.Push(_root);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!node.Bounds.Intersects(extent))
            {
                continue;
            }

            if (node.Entries is not null)
            {
                foreach (var entry in node.Entries)
                {
                    if (entry.Bounds.Intersects(extent))
                    {
                        result.Add(entry.Value);
                    }
                }

                continue;
            }

            if (node.Children is null)
            {
                continue;
            }

            for (var index = node.Children.Length - 1; index >= 0; index--)
            {
                var child = node.Children[index];
                if (child.Bounds.Intersects(extent))
                {
                    pending.Push(child);
                }
            }
        }

        return result;
    }

    private static Node[] BuildLeafLevel(
        SpatialIndexEntry<T>[] entries,
        int nodeCapacity)
    {
        var nodeCount = checked((entries.Length + nodeCapacity - 1) / nodeCapacity);
        var nodes = new Node[nodeCount];

        for (var nodeIndex = 0; nodeIndex < nodeCount; nodeIndex++)
        {
            var offset = nodeIndex * nodeCapacity;
            var count = Math.Min(nodeCapacity, entries.Length - offset);
            var nodeEntries = new SpatialIndexEntry<T>[count];
            Array.Copy(entries, offset, nodeEntries, 0, count);
            nodes[nodeIndex] = Node.CreateLeaf(nodeEntries);
        }

        return nodes;
    }

    private static Node[] BuildParentLevel(Node[] children, int nodeCapacity)
    {
        Array.Sort(children, static (left, right) => CompareBounds(left.Bounds, right.Bounds));
        var parentCount = checked((children.Length + nodeCapacity - 1) / nodeCapacity);
        var parents = new Node[parentCount];

        for (var parentIndex = 0; parentIndex < parentCount; parentIndex++)
        {
            var offset = parentIndex * nodeCapacity;
            var count = Math.Min(nodeCapacity, children.Length - offset);
            var nodeChildren = new Node[count];
            Array.Copy(children, offset, nodeChildren, 0, count);
            parents[parentIndex] = Node.CreateBranch(nodeChildren);
        }

        return parents;
    }

    private static int CompareBounds(Envelope2D left, Envelope2D right)
    {
        var leftCenterX = left.MinX + ((left.MaxX - left.MinX) / 2d);
        var rightCenterX = right.MinX + ((right.MaxX - right.MinX) / 2d);
        var xComparison = leftCenterX.CompareTo(rightCenterX);
        if (xComparison != 0)
        {
            return xComparison;
        }

        var leftCenterY = left.MinY + ((left.MaxY - left.MinY) / 2d);
        var rightCenterY = right.MinY + ((right.MaxY - right.MinY) / 2d);
        return leftCenterY.CompareTo(rightCenterY);
    }

    private static Envelope2D UnionEntries(SpatialIndexEntry<T>[] entries)
    {
        var bounds = entries[0].Bounds;
        for (var index = 1; index < entries.Length; index++)
        {
            bounds = Envelope2D.Union(bounds, entries[index].Bounds);
        }

        return bounds;
    }

    private static Envelope2D UnionChildren(Node[] children)
    {
        var bounds = children[0].Bounds;
        for (var index = 1; index < children.Length; index++)
        {
            bounds = Envelope2D.Union(bounds, children[index].Bounds);
        }

        return bounds;
    }

    private sealed class Node
    {
        private Node(
            Envelope2D bounds,
            SpatialIndexEntry<T>[]? entries,
            Node[]? children)
        {
            Bounds = bounds;
            Entries = entries;
            Children = children;
        }

        public Envelope2D Bounds { get; }

        public SpatialIndexEntry<T>[]? Entries { get; }

        public Node[]? Children { get; }

        public static Node CreateLeaf(SpatialIndexEntry<T>[] entries) =>
            new(UnionEntries(entries), entries, null);

        public static Node CreateBranch(Node[] children) =>
            new(UnionChildren(children), null, children);
    }
}
