using System.Globalization;

namespace ExplorerPilot.App.Localization;

public interface ILocalizationService : IDisposable
{
    AppLanguageMode Preference { get; }

    AppLanguageMode EffectiveLanguage { get; }

    CultureInfo CurrentCulture { get; }

    IReadOnlyList<LanguageOption> AvailableLanguages { get; }

    void Initialize();

    void SetPreference(AppLanguageMode mode);

    string GetString(string key);

    string GetString(string key, params object[] args);

    bool HasKey(string key);

    IReadOnlyCollection<string> GetAllKeys();

    event EventHandler? LanguageChanged;
}
