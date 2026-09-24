using System.Runtime.InteropServices;

namespace SpaceSharp.Services;

/// <summary>Sends files/folders to the Recycle Bin through the Windows shell.</summary>
internal static class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const int FOF_NOCONFIRMATION = 0x0010;
    private const int FOF_ALLOWUNDO = 0x0040;
    private const int FOF_WANTNUKEWARNING = 0x4000; // warn if the item is too large for the Recycle Bin

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    public static bool TrySend(string path, IntPtr owner, out string? error)
    {
        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = FO_DELETE,
            pFrom = path + "\0\0", // must be double-null terminated
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING)
        };

        int result = SHFileOperation(ref op);
        if (result != 0)
        {
            error = $"Windows reported error code 0x{result:X}.";
            return false;
        }

        if (op.fAnyOperationsAborted)
        {
            error = "The operation was cancelled.";
            return false;
        }

        if (File.Exists(path) || Directory.Exists(path))
        {
            error = "The item still exists after the delete operation.";
            return false;
        }

        error = null;
        return true;
    }
}
