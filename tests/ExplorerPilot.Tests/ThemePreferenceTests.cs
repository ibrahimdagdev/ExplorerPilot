using ExplorerPilot.App.Settings;
using ExplorerPilot.App.Theming;

namespace ExplorerPilot.Tests;

public sealed class ThemePreferenceTests
{
    [Fact]
    public void UserSettings_DefaultTheme_IsLight()
    {
        Assert.Equal("Light", new UserSettings().Theme);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotATheme")]
    [InlineData("Purple")]
    public void ParseThemePreference_MissingOrInvalid_FallsBackToLight(string? value)
    {
        Assert.Equal(AppThemeMode.Light, ThemeService.ParseThemePreference(value));
    }

    [Theory]
    [InlineData("Light", AppThemeMode.Light)]
    [InlineData("light", AppThemeMode.Light)]
    [InlineData("Dark", AppThemeMode.Dark)]
    [InlineData("DARK", AppThemeMode.Dark)]
    [InlineData("System", AppThemeMode.System)]
    [InlineData(" system ", AppThemeMode.System)]
    public void ParseThemePreference_PreservesValidSavedValues(string value, AppThemeMode expected)
    {
        Assert.Equal(expected, ThemeService.ParseThemePreference(value));
    }

    [Fact]
    public void ParseThemePreference_DoesNotRewriteDarkToLight()
    {
        Assert.Equal(AppThemeMode.Dark, ThemeService.ParseThemePreference("Dark"));
    }
}
