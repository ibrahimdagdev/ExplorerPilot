using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ExplorerPilot.App.Services;

public sealed class FileActionService : IFileActionService
{
    public bool TryOpenContainingFolder(string fullPath, out string? errorMessage)
    {
        errorMessage = null;

        var plan = ResolveOpenContainingFolder(fullPath);
        if (plan.Kind == OpenFolderKind.Invalid)
        {
            errorMessage = "empty";
            return false;
        }

        if (plan.Kind == OpenFolderKind.Missing)
        {
            errorMessage = "missing";
            return false;
        }

        try
        {
            if (plan.Kind == OpenFolderKind.SelectFile)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + plan.TargetPath + "\"",
                    UseShellExecute = true
                });
                return true;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + plan.TargetPath + "\"",
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception)
        {
            errorMessage = "failed";
            return false;
        }
    }

    public bool TryCopyPath(string fullPath, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(fullPath))
        {
            errorMessage = "empty";
            return false;
        }

        try
        {
            Clipboard.SetText(fullPath);
            return true;
        }
        catch (Exception)
        {
            errorMessage = "failed";
            return false;
        }
    }

    /// <summary>
    /// Resolves how Explorer should be opened without launching a process (testable).
    /// </summary>
    public static OpenFolderPlan ResolveOpenContainingFolder(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return new OpenFolderPlan(OpenFolderKind.Invalid, null);
        }

        if (File.Exists(fullPath))
        {
            return new OpenFolderPlan(OpenFolderKind.SelectFile, fullPath);
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            return new OpenFolderPlan(OpenFolderKind.OpenDirectory, directory);
        }

        return new OpenFolderPlan(OpenFolderKind.Missing, null);
    }

    public readonly record struct OpenFolderPlan(OpenFolderKind Kind, string? TargetPath);

    public enum OpenFolderKind
    {
        Invalid,
        Missing,
        SelectFile,
        OpenDirectory
    }
}
