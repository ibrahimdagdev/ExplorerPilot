using System.Globalization;
using System.Windows;
using ExplorerPilot.App.Converters;
using ExplorerPilot.App.Settings;
using Microsoft.Extensions.Logging;

namespace ExplorerPilot.App.Localization;

public sealed class LocalizationService : ILocalizationService
{
    private const string LocalizationDictionaryTag = "ExplorerPilot.LocalizationDictionary";

    private readonly ILogger<LocalizationService> _logger;
    private readonly UserSettingsStore _settingsStore;
    private readonly object _sync = new();
    private readonly List<LanguageOption> _languages;
    private bool _initialized;
    private bool _disposed;
    private ResourceDictionary? _activeDictionary;

    public LocalizationService(ILogger<LocalizationService> logger, UserSettingsStore settingsStore)
    {
        _logger = logger;
        _settingsStore = settingsStore;
        _languages =
        [
            new LanguageOption(AppLanguageMode.System, "System"),
            new LanguageOption(AppLanguageMode.Turkish, "Türkçe"),
            new LanguageOption(AppLanguageMode.English, "English")
        ];
        AvailableLanguages = _languages;
        CurrentCulture = CultureInfo.GetCultureInfo("en-US");
    }

    public AppLanguageMode Preference { get; private set; } = AppLanguageMode.System;

    public AppLanguageMode EffectiveLanguage { get; private set; } = AppLanguageMode.English;

    public CultureInfo CurrentCulture { get; private set; }

    public IReadOnlyList<LanguageOption> AvailableLanguages { get; }

    public event EventHandler? LanguageChanged;

    public void Initialize()
    {
        lock (_sync)
        {
            if (_initialized)
            {
                return;
            }

            var settings = _settingsStore.Load();
            Preference = ParseLanguage(settings.Language);
            ApplyLanguageCore(force: true);
            _initialized = true;
        }
    }

    public void SetPreference(AppLanguageMode mode)
    {
        lock (_sync)
        {
            if (Preference == mode && _initialized && mode != AppLanguageMode.System)
            {
                return;
            }

            Preference = mode;
            _settingsStore.Update(s => s.Language = mode.ToString());
            ApplyLanguageCore(force: true);
        }
    }

    public string GetString(string key)
    {
        if (TryGetResource(key, out var value) && value is string text && !string.IsNullOrEmpty(text))
        {
            return text;
        }

        _logger.LogWarning("Missing localization key: {Key}", key);
        return key;
    }

    public string GetString(string key, params object[] args)
    {
        var format = GetString(key);
        try
        {
            return string.Format(CurrentCulture, format, args);
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Invalid format for localization key {Key}", key);
            return format;
        }
    }

    public bool HasKey(string key) => TryGetResource(key, out _);

    public IReadOnlyCollection<string> GetAllKeys()
    {
        if (_activeDictionary is null)
        {
            return Array.Empty<string>();
        }

        return _activeDictionary.Keys
            .OfType<string>()
            .Where(k => k != "LocalizationDictionaryTag")
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LocalizationAccessor.Current = null;
    }

    private void ApplyLanguageCore(bool force)
    {
        var resolved = ResolveEffectiveLanguage(Preference);
        if (!force && resolved == EffectiveLanguage)
        {
            RefreshLanguageOptionNames();
            return;
        }

        EffectiveLanguage = resolved;
        var cultureName = resolved == AppLanguageMode.Turkish ? "tr-TR" : "en-US";
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CurrentCulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        var uri = resolved == AppLanguageMode.Turkish
            ? new Uri("Localization/Strings.tr-TR.xaml", UriKind.Relative)
            : new Uri("Localization/Strings.en-US.xaml", UriKind.Relative);

        try
        {
            var app = Application.Current;
            if (app is null)
            {
                return;
            }

            var dictionary = new ResourceDictionary { Source = uri };
            dictionary["LocalizationDictionaryTag"] = LocalizationDictionaryTag;

            var merged = app.Resources.MergedDictionaries;
            for (var i = merged.Count - 1; i >= 0; i--)
            {
                if (IsLocalizationDictionary(merged[i]))
                {
                    merged.RemoveAt(i);
                }
            }

            // Keep after theme brushes, before or with controls — strings can sit after Controls.
            merged.Add(dictionary);
            _activeDictionary = dictionary;
            LocalizationAccessor.Current = this;

            RefreshLanguageOptionNames();
            _logger.LogInformation("Applied {Language} localization (preference: {Preference})", resolved, Preference);
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply language {Language}", resolved);
        }
    }

    private void RefreshLanguageOptionNames()
    {
        foreach (var option in _languages)
        {
            option.DisplayName = option.Mode switch
            {
                AppLanguageMode.System => GetString("Language_System"),
                AppLanguageMode.Turkish => GetString("Language_Turkish"),
                AppLanguageMode.English => GetString("Language_English"),
                _ => option.Mode.ToString()
            };
        }
    }

    private bool TryGetResource(string key, out object? value)
    {
        value = null;
        if (_activeDictionary is not null && _activeDictionary.Contains(key))
        {
            value = _activeDictionary[key];
            return true;
        }

        var app = Application.Current;
        if (app?.TryFindResource(key) is { } found)
        {
            value = found;
            return true;
        }

        return false;
    }

    private static bool IsLocalizationDictionary(ResourceDictionary dictionary)
    {
        if (dictionary.Source is not null)
        {
            var path = dictionary.Source.OriginalString;
            if (path.Contains("Strings.en-US.xaml", StringComparison.OrdinalIgnoreCase)
                || path.Contains("Strings.tr-TR.xaml", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return dictionary.Contains("LocalizationDictionaryTag")
               && Equals(dictionary["LocalizationDictionaryTag"], LocalizationDictionaryTag);
    }

    private static AppLanguageMode ResolveEffectiveLanguage(AppLanguageMode preference)
    {
        if (preference == AppLanguageMode.Turkish)
        {
            return AppLanguageMode.Turkish;
        }

        if (preference == AppLanguageMode.English)
        {
            return AppLanguageMode.English;
        }

        // System: prefer Windows UI language.
        try
        {
            if (IsTurkish(CultureInfo.InstalledUICulture) || IsTurkish(CultureInfo.CurrentUICulture))
            {
                return AppLanguageMode.Turkish;
            }
        }
        catch
        {
            // fall through
        }

        return AppLanguageMode.English;
    }

    private static bool IsTurkish(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase)
        || culture.Name.StartsWith("tr", StringComparison.OrdinalIgnoreCase);

    private static AppLanguageMode ParseLanguage(string? value)
    {
        if (Enum.TryParse<AppLanguageMode>(value, ignoreCase: true, out var mode))
        {
            return mode;
        }

        // Accept culture names from older drafts.
        if (string.Equals(value, "tr-TR", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "tr", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguageMode.Turkish;
        }

        if (string.Equals(value, "en-US", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "en", StringComparison.OrdinalIgnoreCase))
        {
            return AppLanguageMode.English;
        }

        return AppLanguageMode.System;
    }
}
