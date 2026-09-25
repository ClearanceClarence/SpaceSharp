using System.Windows.Media;
using SpaceSharp.Models;
using SpaceSharp.Util;

namespace SpaceSharp.Services;

/// <summary>One row in the side panel. Public properties so the list can bind to them.</summary>
public sealed class TopRow
{
    public string Name { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string SizeText { get; init; } = string.Empty;
    /// <summary>0..1 share of the largest row, drawn as a bar behind the text.</summary>
    public double Fraction { get; init; }
    public double BarWidth => Math.Round(Fraction * 300);
    public Brush Swatch { get; init; } = Brushes.Transparent;
    /// <summary>The file or folder to zoom to, or null for a type row.</summary>
    public FsNode? Node { get; init; }
    /// <summary>Filter text to apply when the row is a file type.</summary>
    public string? FilterText { get; init; }
}

public enum TopListKind
{
    Files,
    Folders,
    Types
}

/// <summary>Builds the "largest files", "largest folders" and "space by type" lists.</summary>
public static class TopLists
{
    private const int MaxRows = 200;

    public static List<TopRow> Build(FsNode root, TopListKind kind, SizeMeasure measure, ColorScheme scheme) => kind switch
    {
        TopListKind.Files => LargestFiles(root, measure),
        TopListKind.Folders => LargestFolders(root, measure),
        _ => ByType(root, measure, scheme)
    };

    private static List<TopRow> LargestFiles(FsNode root, SizeMeasure measure)
    {
        var top = TopN(root.DescendantFiles(), n => n.SizeFor(measure));
        return ToRows(top, measure, n => n.Parent?.FullPath ?? string.Empty);
    }

    private static List<TopRow> LargestFolders(FsNode root, SizeMeasure measure)
    {
        var top = TopN(root.DescendantDirectories().Where(d => !ReferenceEquals(d, root)), n => n.SizeFor(measure));
        return ToRows(top, measure, n => $"{n.FileCount:N0} files · {n.Parent?.FullPath}");
    }

    private static List<TopRow> ByType(FsNode root, SizeMeasure measure, ColorScheme scheme)
    {
        var totals = new Dictionary<string, (long Bytes, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in root.DescendantFiles())
        {
            string ext = file.Extension.Length == 0 ? "(no extension)" : file.Extension;
            totals.TryGetValue(ext, out var t);
            totals[ext] = (t.Bytes + file.SizeFor(measure), t.Count + 1);
        }

        var ordered = totals.OrderByDescending(kv => kv.Value.Bytes).Take(MaxRows).ToList();
        long largest = ordered.Count > 0 ? ordered[0].Value.Bytes : 1;
        var categories = scheme.Categories.ToDictionary(c => c.Category, c => c.Brush);

        return ordered.Select(kv => new TopRow
        {
            Name = kv.Key,
            Detail = $"{kv.Value.Count:N0} files · {Palette.Categorize(kv.Key).ToString().ToLowerInvariant()}",
            SizeText = SizeFormatter.Format(kv.Value.Bytes),
            Fraction = largest > 0 ? (double)kv.Value.Bytes / largest : 0,
            Swatch = categories[Palette.Categorize(kv.Key)],
            FilterText = kv.Key.StartsWith('.') ? "*" + kv.Key : null
        }).ToList();
    }

    private static List<TopRow> ToRows(List<FsNode> nodes, SizeMeasure measure, Func<FsNode, string> detail)
    {
        long largest = nodes.Count > 0 ? nodes[0].SizeFor(measure) : 1;
        return nodes.Select(n => new TopRow
        {
            Name = n.Name,
            Detail = detail(n),
            SizeText = SizeFormatter.Format(n.SizeFor(measure)),
            Fraction = largest > 0 ? (double)n.SizeFor(measure) / largest : 0,
            Node = n
        }).ToList();
    }

    /// <summary>The N largest items without sorting the whole collection (O(n log N)).</summary>
    private static List<FsNode> TopN(IEnumerable<FsNode> items, Func<FsNode, long> size)
    {
        var queue = new PriorityQueue<FsNode, long>(); // min-heap: the smallest of the kept items is on top
        foreach (var item in items)
        {
            long s = size(item);
            if (s <= 0) continue;
            if (queue.Count < MaxRows) queue.Enqueue(item, s);
            else if (queue.TryPeek(out _, out long smallest) && s > smallest)
            {
                queue.Dequeue();
                queue.Enqueue(item, s);
            }
        }

        var result = new List<FsNode>(queue.Count);
        while (queue.Count > 0) result.Add(queue.Dequeue());
        result.Reverse(); // largest first
        return result;
    }
}
