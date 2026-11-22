using System.Globalization;
using System.Xml.Linq;
using ExplorerPilot.App.Converters;
using ExplorerPilot.App.Settings;

namespace ExplorerPilot.Tests;

public sealed class LocalizationTests
{
    [Fact]
    public void StringDictionaries_HaveMatchingKeys()
    {
        var enKeys = LoadKeys("Localization/Strings.en-US.xaml");
        var trKeys = LoadKeys("Localization/Strings.tr-TR.xaml");

        Assert.Equal(enKeys, trKeys);
        Assert.Contains("Button_StartScan", enKeys);
        Assert.False(enKeys.Except(trKeys).Any());
        Assert.False(trKeys.Except(enKeys).Any());
    }

    [Fact]
    public void StringDictionaries_ContainExpectedTranslations()
    {
        var en = LoadMap("Localization/Strings.en-US.xaml");
        var tr = LoadMap("Localization/Strings.tr-TR.xaml");

        Assert.Equal("Start Scan", en["Button_StartScan"]);
        Assert.Equal("Taramayı Başlat", tr["Button_StartScan"]);
        Assert.Equal("Directory Explorer", en["Explorer_Title"]);
        Assert.Equal("Klasör Gezgini", tr["Explorer_Title"]);
        Assert.Equal("Drives", en["Drives_Title"]);
        Assert.Equal("Sürücüler", tr["Drives_Title"]);
        Assert.Equal("Dashboard", en["Dashboard_Title"]);
        Assert.Equal("Gösterge Paneli", tr["Dashboard_Title"]);
        Assert.Contains("Volume capacity", en["Drives_CapacityNote"]);
        Assert.Contains("Birim kapasitesi", tr["Drives_CapacityNote"]);
        // Turkish character coverage across common UI strings (ğ, ü, ş, ı, ö, ç, İ)
        var sample = string.Join('|',
            tr["App_Subtitle"],
            tr["Language_Turkish"],
            tr["Button_StartScan"],
            tr["Button_CancelScan"],
            tr["Explorer_Title"],
            tr["Explorer_EmptySubtitle"],
            tr["Status_Ready"],
            tr["Drive_NotReady"]);
        Assert.Contains("ğ", sample, StringComparison.Ordinal);
        Assert.Contains("ü", sample, StringComparison.Ordinal);
        Assert.Contains("ş", sample, StringComparison.Ordinal);
        Assert.Contains("ı", sample, StringComparison.Ordinal);
        Assert.Contains("ö", sample, StringComparison.Ordinal);
        Assert.Contains("ç", sample, StringComparison.Ordinal);
        Assert.Contains("İ", sample, StringComparison.Ordinal);

    }

    [Fact]
    public void PercentageConverter_FormatsTurkishAndEnglishDifferently()
    {
        var en = PercentageConverter.Format(75.5, CultureInfo.GetCultureInfo("en-US"));
        var tr = PercentageConverter.Format(75.5, CultureInfo.GetCultureInfo("tr-TR"));

        Assert.Equal("75.5%", en);
        Assert.Equal("%75,5", tr);
    }

    [Fact]
    public void ByteSizeConverter_UsesCultureSeparators()
    {
        var bytes = 1536L * 1024 * 1024; // 1.5 GB
        var en = ByteSizeConverter.Format(bytes, CultureInfo.GetCultureInfo("en-US"));
        var tr = ByteSizeConverter.Format(bytes, CultureInfo.GetCultureInfo("tr-TR"));

        Assert.Contains('.', en);
        Assert.Contains(',', tr);
        Assert.EndsWith("GB", en);
        Assert.EndsWith("GB", tr);
    }

    [Fact]
    public void UserSettingsStore_PersistsLanguageWithoutClearingTheme()
    {
        using var temp = TemporaryDirectory.Create();
        var store = new UserSettingsStore(Path.Combine(temp.Path, "settings.json"));
        store.Update(s =>
        {
            s.Theme = "Dark";
            s.Language = "Turkish";
        });

        var loaded = store.Load();
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal("Turkish", loaded.Language);

        store.Update(s => s.Language = "English");
        loaded = store.Load();
        Assert.Equal("Dark", loaded.Theme);
        Assert.Equal("English", loaded.Language);
    }

    [Fact]
    public void SystemLanguage_DetectsTurkishCulture()
    {
        Assert.Equal("tr", CultureInfo.GetCultureInfo("tr-TR").TwoLetterISOLanguageName);
        Assert.Equal("en", CultureInfo.GetCultureInfo("en-US").TwoLetterISOLanguageName);
    }

    [Fact]
    public void MissingTranslationKey_IsDetectableByMapLookup()
    {
        var en = LoadMap("Localization/Strings.en-US.xaml");
        Assert.False(en.ContainsKey("This_Key_Does_Not_Exist"));
        Assert.True(en.ContainsKey("Status_Ready"));
    }

    private static List<string> LoadKeys(string relativePath) =>
        LoadMap(relativePath).Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    private static Dictionary<string, string> LoadMap(string relativePath)
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var fullPath = Path.Combine(
            repoRoot,
            "src",
            "ExplorerPilot.App",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fullPath), $"Missing dictionary at {fullPath}");

        var doc = XDocument.Load(fullPath);
        XNamespace sys = "clr-namespace:System;assembly=System.Runtime";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        return doc.Root!
            .Elements(sys + "String")
            .ToDictionary(
                e => (string)e.Attribute(x + "Key")!,
                e => e.Value,
                StringComparer.Ordinal);
    }

    private static string FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ExplorerPilot.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
