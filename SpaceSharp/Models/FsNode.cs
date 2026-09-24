namespace SpaceSharp.Models;

public enum NodeKind
{
    File,
    Directory
}

/// <summary>A file or folder in the scanned tree.</summary>
public sealed class FsNode
{
    public FsNode(string name, string fullPath, NodeKind kind, FsNode? parent)
    {
        Name = name;
        FullPath = fullPath;
        Kind = kind;
        Parent = parent;
    }

    public string Name { get; }
    public string FullPath { get; }
    public NodeKind Kind { get; }
    public FsNode? Parent { get; private set; }

    /// <summary>Logical size in bytes (recursive for folders).</summary>
    public long Size { get; internal set; }

    /// <summary>Number of files contained (recursive). 1 for a file.</summary>
    public int FileCount { get; internal set; }

    /// <summary>True if the folder (or part of it) could not be read.</summary>
    public bool AccessDenied { get; internal set; }

    /// <summary>Children sorted by size, largest first.</summary>
    public List<FsNode> Children { get; } = new();

    public bool IsDirectory => Kind == NodeKind.Directory;

    public string Extension => IsDirectory ? string.Empty : Path.GetExtension(Name).ToLowerInvariant();

    internal void FinishDirectory()
    {
        long size = 0;
        int files = 0;
        foreach (var child in Children)
        {
            size += child.Size;
            files += child.FileCount;
        }

        Size = size;
        FileCount = files;
        Children.Sort(static (a, b) => b.Size.CompareTo(a.Size));
    }

    /// <summary>Detaches this node and subtracts its size from every ancestor.</summary>
    public void RemoveFromTree()
    {
        var parent = Parent ?? throw new InvalidOperationException("The root node cannot be removed.");
        parent.Children.Remove(this);

        for (var p = parent; p is not null; p = p.Parent)
        {
            p.Size -= Size;
            p.FileCount -= FileCount;
            p.Children.Sort(static (a, b) => b.Size.CompareTo(a.Size));
        }

        Parent = null;
    }

    /// <summary>Finds a folder in this subtree by full path, or null.</summary>
    public FsNode? FindDescendant(string fullPath)
    {
        var node = this;
        while (true)
        {
            if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                return node;

            FsNode? next = null;
            foreach (var child in node.Children)
            {
                if (child.IsDirectory && IsSameOrUnder(fullPath, child.FullPath))
                {
                    next = child;
                    break;
                }
            }

            if (next is null) return null;
            node = next;
        }
    }

    private static bool IsSameOrUnder(string path, string directory) =>
        path.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(directory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => FullPath;
}
