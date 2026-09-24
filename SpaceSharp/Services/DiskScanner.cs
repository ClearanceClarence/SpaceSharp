using System.Collections.Concurrent;
using SpaceSharp.Models;

namespace SpaceSharp.Services;

public readonly record struct ScanProgress(long Files, long Directories, long Bytes, long DeniedFolders, string CurrentPath);

public sealed class ScanOptions
{
    /// <summary>
    /// Open every file to read its link count so hard-linked data (e.g. WinSxS) is counted once.
    /// Accurate but noticeably slower on large drives.
    /// </summary>
    public bool DetectHardLinks { get; init; }

    /// <summary>Include files and folders with the Hidden or System attribute (on by default).</summary>
    public bool IncludeHidden { get; init; } = true;
}

/// <summary>
/// Walks a folder tree on background threads. The top few levels are scanned
/// in parallel, which helps a lot on SSDs.
/// </summary>
public sealed class DiskScanner
{
    private const int ParallelDepth = 3;

    // Files whose allocated size can differ from their length; only these need the extra Win32 call.
    private const FileAttributes SpecialSize = FileAttributes.Compressed | FileAttributes.SparseFile |
                                               FileAttributes.Offline | FileAttributes.ReparsePoint |
                                               (FileAttributes)0x00400000 /* RECALL_ON_DATA_ACCESS (cloud placeholder) */;

    private EnumerationOptions _enumeration = MakeEnumerationOptions(includeHidden: true);

    private static EnumerationOptions MakeEnumerationOptions(bool includeHidden) => new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        // Reparse-point folders are filtered manually; hidden/system depend on the setting.
        AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System
    };

    private long _files;
    private long _directories;
    private long _bytes;
    private long _denied;
    private long _clusterSize = 4096;
    private string _currentPath = string.Empty;
    private ConcurrentDictionary<(uint, uint, uint), byte>? _seenLinks;

    public ScanProgress GetProgress() => new(
        Interlocked.Read(ref _files),
        Interlocked.Read(ref _directories),
        Interlocked.Read(ref _bytes),
        Interlocked.Read(ref _denied),
        Volatile.Read(ref _currentPath));

    public Task<FsNode> ScanAsync(string rootPath, ScanOptions options, CancellationToken ct)
    {
        Interlocked.Exchange(ref _files, 0);
        Interlocked.Exchange(ref _directories, 0);
        Interlocked.Exchange(ref _bytes, 0);
        Interlocked.Exchange(ref _denied, 0);
        Volatile.Write(ref _currentPath, rootPath);
        _clusterSize = NativeFileInfo.GetClusterSize(rootPath);
        _seenLinks = options.DetectHardLinks ? new ConcurrentDictionary<(uint, uint, uint), byte>() : null;
        _enumeration = MakeEnumerationOptions(options.IncludeHidden);

        return Task.Run(() =>
        {
            var root = new DirectoryInfo(rootPath);
            if (!root.Exists)
                throw new DirectoryNotFoundException($"The folder \"{rootPath}\" does not exist.");

            try
            {
                var node = ScanDirectory(root, null, 0, ct);
                if (IsDriveRoot(rootPath))
                    node.AddFreeSpace(new DriveInfo(rootPath).AvailableFreeSpace);
                return node;
            }
            catch (AggregateException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            finally
            {
                _seenLinks = null;
            }
        }, ct);
    }

    public static bool IsDriveRoot(string path)
    {
        try
        {
            string full = Path.GetFullPath(path).TrimEnd('\\') + "\\";
            return string.Equals(Path.GetPathRoot(full), full, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return false;
        }
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
            foreach (var info in dir.EnumerateFileSystemInfos("*", _enumeration))
            {
                if (info is FileInfo file)
                {
                    node.Children.Add(CreateFileNode(file, node));
                    Interlocked.Increment(ref _files);
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
            Interlocked.Increment(ref _denied);
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

    private FsNode CreateFileNode(FileInfo file, FsNode parent)
    {
        long length = file.Length;
        var attributes = file.Attributes;

        bool duplicate = _seenLinks is not null && length > 0 &&
                         NativeFileInfo.IsDuplicateHardLink(file.FullName, _seenLinks);

        long allocated = duplicate ? 0 : AllocatedSize(file.FullName, length, attributes);
        long size = duplicate ? 0 : length;
        Interlocked.Add(ref _bytes, size);

        return new FsNode(file.Name, file.FullName, NodeKind.File, parent)
        {
            Size = size,
            Allocated = allocated,
            FileCount = 1,
            IsHardLinkDuplicate = duplicate,
            LinkedSize = duplicate ? length : 0
        };
    }

    /// <summary>Space the file takes on the volume: compressed size, rounded up to whole clusters.</summary>
    private long AllocatedSize(string path, long length, FileAttributes attributes)
    {
        if (length <= 0) return 0;

        if ((attributes & SpecialSize) != 0)
        {
            long compressed = NativeFileInfo.GetCompressedSize(path);
            if (compressed >= 0) length = compressed;
        }

        long clusters = (length + _clusterSize - 1) / _clusterSize;
        return clusters * _clusterSize;
    }
}
