using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ExplorerPilot.Infrastructure.Services;

/// <summary>
/// Resolves a stable Windows file identity (volume serial + file index) for hard-link detection.
/// </summary>
internal static class WindowsFileIdentity
{
    public static string? TryGetIdentityKey(string path)
    {
        try
        {
            using var handle = CreateFile(
                path,
                dwDesiredAccess: 0,
                dwShareMode: FileShare.ReadWrite | FileShare.Delete,
                lpSecurityAttributes: IntPtr.Zero,
                dwCreationDisposition: FileMode.Open,
                dwFlagsAndAttributes: FILE_FLAG_BACKUP_SEMANTICS,
                hTemplateFile: IntPtr.Zero);

            if (handle.IsInvalid)
            {
                return null;
            }

            if (!GetFileInformationByHandle(handle, out var info))
            {
                return null;
            }

            var fileIndex = ((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow;
            return $"{info.dwVolumeSerialNumber:X8}:{fileIndex:X16}";
        }
        catch
        {
            return null;
        }
    }

    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        FileMode dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }
}
