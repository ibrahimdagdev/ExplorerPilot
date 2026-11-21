using System.Globalization;
using System.Xml.Linq;
using ExplorerPilot.App.Converters;
using ExplorerPilot.App.Localization;
using ExplorerPilot.App.ViewModels;
using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

public sealed class SystemInformationTests
{
    [Fact]
    public async Task ViewModel_MapsMissingValuesToNotAvailable_English()
    {
        var provider = new FakeSystemInformationProvider(CreateSparseModel());
        var localization = FakeLocalization.English();
        var vm = new SystemInformationViewModel(provider, localization, NullLogger<SystemInformationViewModel>.Instance);

        await vm.LoadAsync();

        Assert.False(vm.IsLoading);
        Assert.True(vm.HasData);
        Assert.False(vm.HasError);

        var os = Assert.Single(vm.Sections, s => s.Title == "Operating System");
        Assert.Contains(os.Rows, r => r.Label == "OS Name" && r.Value == "Not Available");
        Assert.Contains(os.Rows, r => r.Label == "Build Number" && r.Value == "Not Available");

        var bios = Assert.Single(vm.Sections, s => s.Title == "BIOS / UEFI");
        Assert.All(bios.Rows, r => Assert.Equal("Not Available", r.Value));

        vm.Detach();
    }

    [Fact]
    public async Task ViewModel_MapsMissingValuesToNotAvailable_Turkish()
    {
        var provider = new FakeSystemInformationProvider(CreateSparseModel());
        var localization = FakeLocalization.Turkish();
        var vm = new SystemInformationViewModel(provider, localization, NullLogger<SystemInformationViewModel>.Instance);

        await vm.LoadAsync();

        Assert.Contains(vm.Sections, s => s.Title == "İşletim Sistemi");
        Assert.Contains(vm.Sections.SelectMany(s => s.Rows), r => r.Value == "Kullanılamıyor");

        vm.Detach();
    }

    [Fact]
    public async Task ViewModel_SupportsMultipleGraphicsAdapters()
    {
        var model = CreateSparseModel() with
        {
            GraphicsAdapters =
            [
                new GraphicsAdapterInfo { Name = "GPU A", Manufacturer = "Vendor A", AdapterRamBytes = 2UL * 1024 * 1024 * 1024 },
                new GraphicsAdapterInfo { Name = "GPU B", Manufacturer = "Vendor B" }
            ]
        };

        var provider = new FakeSystemInformationProvider(model);
        var localization = FakeLocalization.English();
        var vm = new SystemInformationViewModel(provider, localization, NullLogger<SystemInformationViewModel>.Instance);

        await vm.LoadAsync();

        Assert.Contains(vm.Sections, s => s.Title == "Graphics Card 1");
        Assert.Contains(vm.Sections, s => s.Title == "Graphics Card 2");
        var gpu1 = vm.Sections.First(s => s.Title == "Graphics Card 1");
        Assert.Contains(gpu1.Rows, r => r.Label == "Name" && r.Value == "GPU A");
        Assert.Contains(gpu1.Rows, r => r.Label == "Reported Video Memory" && r.Value.Contains("GB", StringComparison.Ordinal));

        vm.Detach();
    }

    [Fact]
    public async Task ViewModel_RefreshReloadsProvider()
    {
        var provider = new FakeSystemInformationProvider(CreateSparseModel());
        var localization = FakeLocalization.English();
        var vm = new SystemInformationViewModel(provider, localization, NullLogger<SystemInformationViewModel>.Instance);

        await vm.LoadAsync();
        Assert.Equal(1, provider.CallCount);

        Assert.True(vm.RefreshCommand.CanExecute(null));
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, provider.CallCount);

        vm.Detach();
    }

    [Fact]
    public async Task ViewModel_HandlesProviderFailure()
    {
        var provider = new FakeSystemInformationProvider(null, fail: true);
        var localization = FakeLocalization.English();
        var vm = new SystemInformationViewModel(provider, localization, NullLogger<SystemInformationViewModel>.Instance);

        await vm.LoadAsync();

        Assert.True(vm.HasError);
        Assert.False(vm.HasData);
        Assert.Contains("Unable to collect", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        vm.Detach();
    }

    [Fact]
    public async Task ViewModel_LanguageSwitchRebuildsLabelsWithoutReload()
    {
        var provider = new FakeSystemInformationProvider(CreateSparseModel());
        var localization = FakeLocalization.English();
        var vm = new SystemInformationViewModel(provider, localization, NullLogger<SystemInformationViewModel>.Instance);

        await vm.LoadAsync();
        Assert.Equal(1, provider.CallCount);
        Assert.Equal("System Information", vm.WindowTitle);

        localization.SwitchToTurkish();

        Assert.Equal(1, provider.CallCount);
        Assert.Equal("Sistem Bilgileri", vm.WindowTitle);
        Assert.Contains(vm.Sections, s => s.Title == "İşlemci");

        vm.Detach();
    }

    [Fact]
    public async Task ViewModel_ShowsPartialWarningWhenProviderReportsWarnings()
    {
        var model = CreateSparseModel() with { Warnings = ["Win32_BIOS"] };
        var provider = new FakeSystemInformationProvider(model);
        var localization = FakeLocalization.English();
        var vm = new SystemInformationViewModel(provider, localization, NullLogger<SystemInformationViewModel>.Instance);

        await vm.LoadAsync();

        Assert.True(vm.HasWarning);
        Assert.Contains("could not be read", vm.WarningMessage, StringComparison.OrdinalIgnoreCase);

        vm.Detach();
    }

    [Fact]
    public void ByteSizeConverter_FormatsRamCultureAware()
    {
        var bytes = 16L * 1024 * 1024 * 1024;
        var en = ByteSizeConverter.Format(bytes, CultureInfo.GetCultureInfo("en-US"));
        var tr = ByteSizeConverter.Format(bytes, CultureInfo.GetCultureInfo("tr-TR"));

        Assert.Equal("16 GB", en);
        Assert.Equal("16 GB", tr);
    }

    [Fact]
    public void StringDictionaries_IncludeSystemInfoKeys()
    {
        var en = LoadMap("Localization/Strings.en-US.xaml");
        var tr = LoadMap("Localization/Strings.tr-TR.xaml");

        Assert.Equal("System Information", en["SystemInfo_Title"]);
        Assert.Equal("Sistem Bilgileri", tr["SystemInfo_Title"]);
        Assert.Equal("Not Available", en["SystemInfo_NotAvailable"]);
        Assert.Equal("Kullanılamıyor", tr["SystemInfo_NotAvailable"]);
        Assert.Equal("Physical Cores", en["SystemInfo_PhysicalCores"]);
        Assert.Equal("Fiziksel Çekirdekler", tr["SystemInfo_PhysicalCores"]);
    }

    private static SystemInformationModel CreateSparseModel() =>
        new()
        {
            OperatingSystem = new OperatingSystemInfo
            {
                OsArchitecture = "x64",
                ProcessArchitecture = "x64"
            },
            Processor = new ProcessorInfo { LogicalProcessorCount = 8 },
            Memory = new MemoryInfo(),
            Motherboard = new MotherboardInfo(),
            Bios = new BiosInfo(),
            Computer = new ComputerSystemInfo { SystemArchitecture = "x64" },
            GraphicsAdapters = [],
            StorageDevices = [],
            Warnings = [],
            CollectedAtUtc = DateTimeOffset.UtcNow
        };

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

    private sealed class FakeSystemInformationProvider : ISystemInformationProvider
    {
        private readonly SystemInformationModel? _model;
        private readonly bool _fail;

        public FakeSystemInformationProvider(SystemInformationModel? model, bool fail = false)
        {
            _model = model;
            _fail = fail;
        }

        public int CallCount { get; private set; }

        public Task<SystemInformationModel> GetSystemInformationAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_fail)
            {
                throw new InvalidOperationException("Simulated WMI failure");
            }

            return Task.FromResult(_model ?? CreateSparseModel());
        }
    }

    private sealed class FakeLocalization : ILocalizationService
    {
        private readonly Dictionary<string, string> _strings;
        private AppLanguageMode _mode;

        private FakeLocalization(AppLanguageMode mode, Dictionary<string, string> strings)
        {
            _mode = mode;
            _strings = strings;
            Preference = mode;
            EffectiveLanguage = mode;
            CurrentCulture = mode == AppLanguageMode.Turkish
                ? CultureInfo.GetCultureInfo("tr-TR")
                : CultureInfo.GetCultureInfo("en-US");
            AvailableLanguages = [];
        }

        public static FakeLocalization English() =>
            new(AppLanguageMode.English, LoadMap("Localization/Strings.en-US.xaml"));

        public static FakeLocalization Turkish() =>
            new(AppLanguageMode.Turkish, LoadMap("Localization/Strings.tr-TR.xaml"));

        public AppLanguageMode Preference { get; private set; }

        public AppLanguageMode EffectiveLanguage { get; private set; }

        public CultureInfo CurrentCulture { get; private set; }

        public IReadOnlyList<LanguageOption> AvailableLanguages { get; }

        public event EventHandler? LanguageChanged;

        public void Initialize()
        {
        }

        public void SetPreference(AppLanguageMode mode) => SwitchTo(mode);

        public void SwitchToTurkish() => SwitchTo(AppLanguageMode.Turkish);

        public string GetString(string key) =>
            _strings.TryGetValue(key, out var value) ? value : key;

        public string GetString(string key, params object[] args) =>
            string.Format(CurrentCulture, GetString(key), args);

        public bool HasKey(string key) => _strings.ContainsKey(key);

        public IReadOnlyCollection<string> GetAllKeys() => _strings.Keys.ToList();

        public void Dispose()
        {
        }

        private void SwitchTo(AppLanguageMode mode)
        {
            _mode = mode;
            Preference = mode;
            EffectiveLanguage = mode;
            CurrentCulture = mode == AppLanguageMode.Turkish
                ? CultureInfo.GetCultureInfo("tr-TR")
                : CultureInfo.GetCultureInfo("en-US");

            var map = mode == AppLanguageMode.Turkish
                ? LoadMap("Localization/Strings.tr-TR.xaml")
                : LoadMap("Localization/Strings.en-US.xaml");

            _strings.Clear();
            foreach (var pair in map)
            {
                _strings[pair.Key] = pair.Value;
            }

            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
