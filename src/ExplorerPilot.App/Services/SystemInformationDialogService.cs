using System.Windows;
using ExplorerPilot.App.ViewModels;
using ExplorerPilot.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace ExplorerPilot.App.Services;

public sealed class SystemInformationDialogService : ISystemInformationDialogService
{
    private readonly IServiceProvider _services;
    private SystemInformationWindow? _openWindow;

    public SystemInformationDialogService(IServiceProvider services)
    {
        _services = services;
    }

    public void Show(Window? owner)
    {
        if (_openWindow is { IsVisible: true })
        {
            _openWindow.Owner = owner ?? _openWindow.Owner;
            if (_openWindow.WindowState == WindowState.Minimized)
            {
                _openWindow.WindowState = WindowState.Normal;
            }

            _openWindow.Activate();
            return;
        }

        var viewModel = _services.GetRequiredService<SystemInformationViewModel>();
        var window = _services.GetRequiredService<SystemInformationWindow>();
        window.DataContext = viewModel;
        window.Owner = owner ?? Application.Current?.MainWindow;
        window.Closed += (_, _) =>
        {
            viewModel.Detach();
            if (ReferenceEquals(_openWindow, window))
            {
                _openWindow = null;
            }
        };

        _openWindow = window;
        window.Show();
        window.Activate();
        _ = viewModel.LoadAsync();
    }
}
