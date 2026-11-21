using System.Windows;

namespace ExplorerPilot.App.Services;

public interface ISystemInformationDialogService
{
    void Show(Window? owner);
}
