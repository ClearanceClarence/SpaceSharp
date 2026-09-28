using System.IO;
using System.IO.Compression;
using System.Text;
using SpaceSharp.Models;

namespace SpaceSharp.Services;

/// <summary>What a saved scan knows about itself, readable without loading the tree.</summary>
public sealed record ScanFileInfo(string RootPath, DateTime ScannedUtc, string Method, long FreeBytes, int FileCount, long Bytes, string Path);

/// <summary>
/// Saves and loads a scanned tree as a compact file (.sscan): a gzip stream of the tree in preorder,
/// one record per item. Loading a million-file drive takes well under a second, which is what makes
/// "open the last map instantly" and "compare with last time" possible.
/// </summary>
public static class ScanFile
{
    public const string Extension = ".sscan";
    private const uint Magic = 0x4E435353; // "SSCN"
    private const byte Version = 1;

    private const byte KindFile = 0, KindDirectory = 1, KindHardLink = 2;

    /// <summary>%LocalAppData%\SpaceSharp\scans, created on demand.</summary>
    public static string Folder
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceSharp", "scans");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>The automatic save for a scan root: "C.sscan" for a drive, a hash-named file for a folder.</summary>
    public static string AutoPathFor(string rootPath)
    {
        string trimmed = rootPath.TrimEnd('\\');
        string name = trimmed.Length <= 2 && trimmed.Length > 0 && char.IsLetter(trimmed[0])
            ? trimmed[..1].ToUpperInvariant()
            : "folder-" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(trimmed.ToUpperInvariant())))[..10];
        return Path.Combine(Folder, name + Extension);
    }

    /// <summary>The copy kept of the previous automatic save, used as the comparison baseline.</summary>
    public static string PreviousPathFor(string rootPath) => Path.ChangeExtension(AutoPathFor(rootPath), ".prev" + Extension);

    // ------------------------------------------------------------------ write

    public static void Save(FsNode root, string path, DateTime scannedUtc, string method)
    {
        string temp = path + ".tmp";
        using (var file = File.Create(temp))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var w = new BinaryWriter(gzip, Encoding.UTF8))
        {
            w.Write(Magic);
            w.Write(Version);
            w.Write(root.FullPath);
            w.Write(scannedUtc.Ticks);
            w.Write(method);
            w.Write(root.FreeBytes);
            w.Write(root.FileCount);
            w.Write(root.Size - (root.FreeSpaceVisible ? root.FreeBytes : 0));
            foreach (var child in root.Children) WriteNode(w, child);
            w.Write(byte.MaxValue); // end of root's children
        }
        File.Move(temp, path, overwrite: true);
    }

    private static void WriteNode(BinaryWriter w, FsNode node)
    {
        if (node.IsFreeSpace || node.IsGroup) return;
        if (node.IsDirectory)
        {
            w.Write(KindDirectory);
            w.Write(node.Name);
            w.Write(node.LastWriteUtc.Ticks);
            w.Write(node.AccessDenied);
            foreach (var child in node.Children) WriteNode(w, child);
            w.Write(byte.MaxValue);
        }
        else
        {
            w.Write(node.IsHardLinkDuplicate ? KindHardLink : KindFile);
            w.Write(node.Name);
            w.Write(node.IsHardLinkDuplicate ? node.LinkedSize : node.Size);
            w.Write(node.Allocated);
            w.Write(node.LastWriteUtc.Ticks);
        }
    }

    // ------------------------------------------------------------------ read

    public static ScanFileInfo? Peek(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var r = new BinaryReader(gzip, Encoding.UTF8);
            if (r.ReadUInt32() != Magic || r.ReadByte() != Version) return null;
            string root = r.ReadString();
            var when = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
            string method = r.ReadString();
            long free = r.ReadInt64();
            int files = r.ReadInt32();
            long bytes = r.ReadInt64();
            return new ScanFileInfo(root, when, method, free, files, bytes, path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static (FsNode Root, ScanFileInfo Info) Load(string path, bool addFreeSpace = true)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var r = new BinaryReader(gzip, Encoding.UTF8);
        if (r.ReadUInt32() != Magic) throw new InvalidDataException("Not a SpaceSharp scan file.");
        if (r.ReadByte() != Version) throw new InvalidDataException("This scan file was written by a newer SpaceSharp.");
        string rootPath = r.ReadString();
        var when = new DateTime(r.ReadInt64(), DateTimeKind.Utc);
        string method = r.ReadString();
        long free = r.ReadInt64();
        int files = r.ReadInt32();
        long bytes = r.ReadInt64();

        var root = new FsNode(rootPath, rootPath, NodeKind.Directory, null);
        ReadChildren(r, root);
        root.FinishDirectory();
        if (addFreeSpace && free > 0) root.AddFreeSpace(free);
        return (root, new ScanFileInfo(rootPath, when, method, free, files, bytes, path));
    }

    private static void ReadChildren(BinaryReader r, FsNode folder)
    {
        while (true)
        {
            byte kind = r.ReadByte();
            if (kind == byte.MaxValue) return;
            string name = r.ReadString();
            string path = folder.FullPath.EndsWith('\\') ? folder.FullPath + name : folder.FullPath + "\\" + name;
            if (kind == KindDirectory)
            {
                var dir = new FsNode(name, path, NodeKind.Directory, folder)
                {
                    LastWriteUtc = new DateTime(r.ReadInt64(), DateTimeKind.Utc),
                    AccessDenied = r.ReadBoolean()
                };
                folder.Children.Add(dir);
                ReadChildren(r, dir);
                dir.FinishDirectory();
            }
            else
            {
                long size = r.ReadInt64(), allocated = r.ReadInt64();
                var ticks = r.ReadInt64();
                bool link = kind == KindHardLink;
                folder.Children.Add(new FsNode(name, path, NodeKind.File, folder)
                {
                    Size = link ? 0 : size,
                    Allocated = link ? 0 : allocated,
                    LinkedSize = link ? size : 0,
                    IsHardLinkDuplicate = link,
                    FileCount = 1,
                    LastWriteUtc = new DateTime(ticks, DateTimeKind.Utc)
                });
            }
        }
    }
}
