namespace ExplorerPilot.App.Settings;

public sealed class UserSettings
{
    /// <summary>Persisted theme preference. Default Light for first-run / missing settings.</summary>
    public string Theme { get; set; } = "Light";

    public string Language { get; set; } = "System";

    /// <summary>When true, the left navigation sidebar shows labels; when false, icons only.</summary>
    public bool SidebarExpanded { get; set; } = true;
}
