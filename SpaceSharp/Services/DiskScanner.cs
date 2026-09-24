using SpaceSharp.Models;

namespace SpaceSharp.Services;

public readonly record struct ScanProgress(long Files, long Directories, long Bytes, string CurrentPath);

/// <summary>
/// Walks a folder tree on background threads. The top few levels are scanned
/// in parallel, which helps a lot on SSDs.
/// </summary>
public sealed class DiskScanner
{
    private const int ParallelDepth = 3;

    private static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = 0 // include hidden and system files; reparse-point folders are filtered manually
    };

    private long _files;
    private long _directories;
    private long _bytes;
    private string _currentPath = string.Empty;

    public ScanProgress GetProgress() => new(
        Interlocked.Read(ref _files),
        Interlocked.Read(ref _directories),
        Interlocked.Read(ref _bytes),
        Volatile.Read(ref _currentPath));

    public Task<FsNode> ScanAsync(string rootPath, CancellationToken ct)
    {
        Interlocked.Exchange(ref _files, 0);
        Interlocked.Exchange(ref _directories, 0);
        Interlocked.Exchange(ref _bytes, 0);
        Volatile.Write(ref _currentPath, rootPath);

        return Task.Run(() =>
        {
            var root = new DirectoryInfo(rootPath);
            if (!root.Exists)
                throw new DirectoryNotFoundException($"The folder \"{rootPath}\" does not exist.");

            try
            {
                return ScanDirectory(root, null, 0, ct);
            }
            catch (AggregateException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
        }, ct);
    }

    private FsNode ScanDirectory(DirectoryInfo dir, FsNode? parent, int depth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var node = new FsNode(parent is null ? dir.FullName : dir.Name, dir.FullName, NodeKind.Directory, parent);
        Volatile.Write(ref _currentPath, dir.FullName);
        Interlocked.Increment(ref _directories);

        var subdirectories = new List<DirectoryInfo>();
        try
        {
            foreach (var info in dir.EnumerateFileSystemInfos("*", Options))
            {
                if (info is FileInfo file)
                {
                    long length = file.Length;
                    node.Children.Add(new FsNode(file.Name, file.FullName, NodeKind.File, node)
                    {
                        Size = length,
                        FileCount = 1
                    });
                    Interlocked.Increment(ref _files);
                    Interlocked.Add(ref _bytes, length);
                }
                else if (info is DirectoryInfo sub && (sub.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    // Skipping junctions/symlinks avoids loops and double counting.
                    subdirectories.Add(sub);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            node.AccessDenied = true;
        }

        var subNodes = new FsNode[subdirectories.Count];
        if (depth < ParallelDepth && subdirectories.Count > 1)
        {
            Parallel.For(0, subdirectories.Count, new ParallelOptions { CancellationToken = ct },
                i => subNodes[i] = ScanDirectory(subdirectories[i], node, depth + 1, ct));
        }
        else
        {
            for (int i = 0; i < subdirectories.Count; i++)
                subNodes[i] = ScanDirectory(subdirectories[i], node, depth + 1, ct);
        }

        node.Children.AddRange(subNodes);
        node.FinishDirectory();
        return node;
    }
}
