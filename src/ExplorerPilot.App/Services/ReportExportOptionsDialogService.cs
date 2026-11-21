using System.Windows;
using ExplorerPilot.App.ViewModels;
using ExplorerPilot.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace ExplorerPilot.App.Services;

public sealed class ReportExportOptionsDialogService : IReportExportOptionsDialogService
{
    private readonly IServiceProvider _services;

    public ReportExportOptionsDialogService(IServiceProvider services)
    {
        _services = services;
    }

    public ReportExportDialogResult? Show(Window? owner)
    {
        var viewModel = _services.GetRequiredService<ReportExportOptionsViewModel>();
        var window = _services.GetRequiredService<ReportExportOptionsWindow>();
        window.DataContext = viewModel;
        window.Owner = owner ?? Application.Current?.MainWindow;

        viewModel.AttachClose(result =>
        {
            // Assigning DialogResult closes a modal window.
            window.DialogResult = result;
        });

        var accepted = window.ShowDialog() == true && viewModel.Confirmed;
        if (!accepted)
        {
            return null;
        }

        return new ReportExportDialogResult
        {
            Format = viewModel.SelectedFormat,
            IncludeDirectoryTree = viewModel.IncludeDirectoryTree,
            IncludeCategories = viewModel.IncludeCategories,
            IncludeExtensions = viewModel.IncludeExtensions,
            IncludeErrors = viewModel.IncludeErrors,
            MaxLargestFiles = viewModel.MaxLargestFiles,
            MaxLargestFolders = viewModel.MaxLargestFolders
        };
    }
}
