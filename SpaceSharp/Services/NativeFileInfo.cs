using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32.SafeHandles;

namespace SpaceSharp.Services;

/// <summary>Win32 calls for size on disk and hard-link detection.</summary>
internal static class NativeFileInfo
{
    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint FILE_SHARE_ALL = 0x0001 | 0x0002 | 0x0004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public FILETIME CreationTime;
        public FILETIME LastAccessTime;
        public FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string fileName, out uint fileSizeHigh);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceW(string rootPath, out uint sectorsPerCluster, out uint bytesPerSector,
        out uint freeClusters, out uint totalClusters);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out BY_HANDLE_FILE_INFORMATION info);

    /// <summary>Cluster size of the volume that holds the path; 4096 if it can't be read.</summary>
    public static long GetClusterSize(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(root) && GetDiskFreeSpaceW(root, out uint perCluster, out uint perSector, out _, out _))
            {
                long size = (long)perCluster * perSector;
                if (size > 0) return size;
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
        }
        return 4096;
    }

    /// <summary>Compressed size of a file (what compressed/sparse files really use), or -1.</summary>
    public static long GetCompressedSize(string path)
    {
        uint low = GetCompressedFileSizeW(path, out uint high);
        if (low == uint.MaxValue && Marshal.GetLastWin32Error() != 0) return -1;
        return ((long)high << 32) | low;
    }

    /// <summary>
    /// True if the file has several hard links and one of them was already seen in this scan.
    /// Opens the file for attribute reading only.
    /// </summary>
    public static bool IsDuplicateHardLink(string path, ConcurrentDictionary<(uint Volume, uint High, uint Low), byte> seen)
    {
        using var handle = CreateFileW(path, FILE_READ_ATTRIBUTES, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (handle.IsInvalid) return false;

        if (!GetFileInformationByHandle(handle, out var info) || info.NumberOfLinks <= 1) return false;
        return !seen.TryAdd((info.VolumeSerialNumber, info.FileIndexHigh, info.FileIndexLow), 0);
    }
}
