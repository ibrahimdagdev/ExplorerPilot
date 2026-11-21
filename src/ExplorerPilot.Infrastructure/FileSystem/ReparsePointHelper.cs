namespace ExplorerPilot.Infrastructure.FileSystem;

internal static class ReparsePointHelper
{
    public static bool IsDirectoryReparsePoint(FileAttributes attributes)
    {
        return attributes.HasFlag(FileAttributes.Directory)
               && attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    public static bool IsFileReparsePoint(FileAttributes attributes)
    {
        return !attributes.HasFlag(FileAttributes.Directory)
               && attributes.HasFlag(FileAttributes.ReparsePoint);
    }
}
