using System.Windows;
using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.App.Services;

public sealed class ReportExportDialogResult
{
    public required ReportFormat Format { get; init; }

    public bool IncludeDirectoryTree { get; init; }

    public bool IncludeCategories { get; init; }

    public bool IncludeExtensions { get; init; }

    public bool IncludeErrors { get; init; }

    public int MaxLargestFiles { get; init; }

    public int MaxLargestFolders { get; init; }
}

public interface IReportExportOptionsDialogService
{
    /// <summary>
    /// Shows the report options dialog. Returns null when the user cancels.
    /// </summary>
    ReportExportDialogResult? Show(Window? owner);
}
