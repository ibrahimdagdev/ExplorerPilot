using System.Globalization;
using ExplorerPilot.App.Localization;
using ExplorerPilot.App.Settings;
using ExplorerPilot.App.ViewModels;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Infrastructure.Scanning;

namespace ExplorerPilot.Tests;

public sealed class DriveScanIntegrationTests
{
    [Fact]
    public async Task ScanDriveRoot_ViaDiskScanner_CompletesAndFeedsTree()
    {
        using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "docs"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "docs", "a.txt"), "hello");
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "big.bin"), new byte[128]);

        // Simulate "scan drive" by scanning a controlled root with the default optimized scanner.
        var scanner = new DiskScanner();
        Assert.Equal(DiskScannerEnumerationMode.FileSystemInfoMetadata, scanner.EnumerationMode);

        var result = await scanner.ScanAsync(temp.Path);
        Assert.Equal(ScanPhase.Completed, result.FinalStatus);
        Assert.True(result.TotalFiles >= 2);

        var tree = new TreeNodeViewModel(result.RootNode, isRoot: true) { IsExpanded = true };
        Assert.True(tree.AreChildrenMaterialized);
        Assert.Contains(tree.Children, c => c.Name is "big.bin" or "docs");
    }

    [Fact]
    public async Task ScanDrive_Cancellation_DoesNotThrow()
    {
        using var temp = TemporaryDirectory.Create();
        for (var i = 0; i < 200; i++)
        {
            var dir = Path.Combine(temp.Path, $"d{i:D3}");
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(Path.Combine(dir, "f.bin"), new byte[8]);
        }

        using var cts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(_ =>
        {
            try
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
            }
        });

        var result = await new DiskScanner().ScanAsync(temp.Path, progress, cts.Token);
        Assert.True(result.FinalStatus is ScanPhase.Cancelled or ScanPhase.Completed);
    }

    [Fact]
    public void DriveCard_RefreshFormatting_UsesLocalizationKeys()
    {
        var drive = new DriveInformation
        {
            Name = @"D:\",
            RootPath = @"D:\",
            VolumeLabel = "Data",
            Kind = VolumeDriveKind.Fixed,
            Status = DriveReadyStatus.Ready,
            FileSystem = "NTFS",
            TotalSizeBytes = 1000,
            AvailableFreeSpaceBytes = 250,
            UsedSpaceBytes = 750,
            UsageRatio = 0.75,
            UsagePercentage = 75
        };

        var card = new DriveCardViewModel(drive);
        var loc = new StubLoc();
        card.RefreshFormatting(loc);

        Assert.Contains("D:", card.TitleText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Drives_Kind_Fixed", card.KindText);
        Assert.Equal("Drives_Status_Ready", card.StatusText);
        Assert.False(string.IsNullOrWhiteSpace(card.TotalText));
        Assert.True(card.CanScan);
    }

    [Fact]
    public void DriveCard_NotReady_CannotScan()
    {
        var card = new DriveCardViewModel(new DriveInformation
        {
            Name = @"E:\",
            RootPath = @"E:\",
            Kind = VolumeDriveKind.Removable,
            Status = DriveReadyStatus.NotReady
        });

        Assert.False(card.CanScan);
        Assert.False(card.IsReady);
    }

    [Fact]
    public void StaleDiscoveryVersion_IsIgnoredByVersionGuardPattern()
    {
        // Documents the MainViewModel discovery version guard contract used for refresh races.
        var version = 0;
        var applied = 0;
        void Apply(int candidate)
        {
            if (candidate != version)
            {
                return;
            }

            applied++;
        }

        version = 1;
        Apply(1);
        version = 2;
        Apply(1); // stale
        Apply(2);

        Assert.Equal(2, applied);
    }

    private sealed class StubLoc : ILocalizationService
    {
        public AppLanguageMode Preference => AppLanguageMode.English;
        public AppLanguageMode EffectiveLanguage => AppLanguageMode.English;
        public CultureInfo CurrentCulture { get; } = CultureInfo.GetCultureInfo("en-US");
        public IReadOnlyList<LanguageOption> AvailableLanguages { get; } = [];
        public event EventHandler? LanguageChanged;
        public void Initialize() { }
        public void SetPreference(AppLanguageMode mode) => LanguageChanged?.Invoke(this, EventArgs.Empty);
        public void SwitchToTurkish() { }
        public string GetString(string key) => key;
        public string GetString(string key, params object[] args) => string.Format(CurrentCulture, key, args);
        public bool HasKey(string key) => true;
        public IReadOnlyCollection<string> GetAllKeys() => [];
        public void Dispose() { }
    }
}
