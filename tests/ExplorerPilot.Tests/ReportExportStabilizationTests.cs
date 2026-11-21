using System.Text;
using System.Text.Json;
using ExplorerPilot.App.Localization;
using ExplorerPilot.App.ViewModels;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Reporting;
using ExplorerPilot.Infrastructure.Reporting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

public sealed class ReportExportStabilizationTests
{
    [Fact]
    public void ReportOptions_ValidateLimits()
    {
        var vm = new ReportExportOptionsViewModel(new StubLocalization());
        vm.MaxLargestFiles = -1;
        Assert.False(vm.TryValidate(out _));

        vm.MaxLargestFiles = 100;
        vm.MaxLargestFolders = 10_001;
        Assert.False(vm.TryValidate(out _));

        vm.MaxLargestFolders = 250;
        Assert.True(vm.TryValidate(out var message));
        Assert.Equal(string.Empty, message);
    }

    [Fact]
    public void Builder_OmitsOptionalSections_WhenDisabled()
    {
        var scan = CreateScanWithStats();
        var report = DiskAnalysisReportBuilder.Build(
            scan,
            new ReportBuildOptions
            {
                IncludeCategories = false,
                IncludeExtensions = false,
                IncludeErrors = false,
                IncludeDirectoryTree = false,
                MaxLargestFiles = 10,
                MaxLargestFolders = 10
            });

        Assert.Empty(report.Categories);
        Assert.Empty(report.Extensions);
        Assert.Empty(report.Errors);
        Assert.Null(report.DirectoryTree);
        Assert.True(report.LargestFiles.Count <= 10);
    }

    [Fact]
    public async Task HtmlStreaming_HandlesUnicodeAndLongPaths_WithoutCdn()
    {
        var service = new ReportExportService(NullLogger<ReportExportService>.Instance);
        var path = TempPath(".html");
        try
        {
            var weirdName = "报告_δοκιμή_<tag>&\"quotes\".txt";
            var longDir = new string('a', 180);
            var root = Dir("kök", Dir(longDir, MakeFile(weirdName, 42)));
            var scan = new ScanResult
            {
                RootNode = root,
                TotalFiles = 1,
                TotalDirectories = 2,
                TotalBytes = 42,
                Duration = TimeSpan.FromMilliseconds(12),
                FinalStatus = ScanPhase.Completed,
                LargestFiles =
                [
                    new LargestFileInfo
                    {
                        Name = weirdName,
                        FullPath = root.Children[0].Children[0].FullPath,
                        SizeBytes = 42,
                        Extension = ".txt"
                    }
                ]
            };

            var result = await service.ExportAsync(new ReportExportOptions
            {
                ScanResult = scan,
                Format = ReportFormat.Html,
                DestinationPath = path,
                IncludeDirectoryTree = true,
                ScanSessionId = Guid.NewGuid()
            });

            Assert.True(result.Succeeded);
            var html = await File.ReadAllTextAsync(path, Encoding.UTF8);
            Assert.Contains("<!DOCTYPE html>", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cdn.", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<script src=", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("&lt;tag&gt;", html, StringComparison.Ordinal);
            Assert.Contains("&amp;", html, StringComparison.Ordinal);
            Assert.Contains("报告", html, StringComparison.Ordinal);
            Assert.Contains("δοκιμή", html, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task HtmlLargeTree_StreamsWithoutHoldingGiantString()
    {
        var service = new ReportExportService(NullLogger<ReportExportService>.Instance);
        var path = TempPath(".html");
        try
        {
            var children = Enumerable.Range(0, 2_500)
                .Select(i => MakeFile($"n{i}.bin", 10 + i))
                .ToArray();
            var root = Dir("root", children);
            var scan = new ScanResult
            {
                RootNode = root,
                TotalFiles = children.Length,
                TotalDirectories = 1,
                TotalBytes = root.SizeBytes,
                Duration = TimeSpan.FromSeconds(1),
                FinalStatus = ScanPhase.Completed
            };

            var before = GC.GetTotalMemory(forceFullCollection: true);
            var result = await service.ExportAsync(new ReportExportOptions
            {
                ScanResult = scan,
                Format = ReportFormat.Html,
                DestinationPath = path,
                IncludeDirectoryTree = true,
                IncludeCategories = false,
                IncludeExtensions = false,
                MaxLargestFiles = 50,
                MaxLargestFolders = 10,
                ScanSessionId = Guid.NewGuid()
            });
            var after = GC.GetTotalMemory(forceFullCollection: false);

            Assert.True(result.Succeeded);
            Assert.True(new FileInfo(path).Length > 50_000);
            // Soft ceiling: streaming should not balloon by tens of MB for this fan-out.
            Assert.True(after - before < 80_000_000);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task FailedExport_PreservesExistingDestination()
    {
        var path = TempPath(".json");
        await File.WriteAllTextAsync(path, "{\"keep\":true}");
        var original = await File.ReadAllTextAsync(path);

        var service = new ReportExportService(NullLogger<ReportExportService>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.ExportAsync(
            new ReportExportOptions
            {
                ScanResult = CreateScanWithStats(),
                Format = ReportFormat.Json,
                DestinationPath = path,
                IncludeDirectoryTree = true,
                ScanSessionId = Guid.NewGuid()
            },
            cancellationToken: cts.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        TryDelete(path);
    }

    [Fact]
    public void AtomicReplace_OverwritesWithoutDeletingFirst()
    {
        var dest = TempPath(".csv");
        var temp = dest + ".tmp";
        try
        {
            File.WriteAllText(dest, "old");
            File.WriteAllText(temp, "new-content");
            ReportExportService.ReplaceAtomically(temp, dest);
            Assert.Equal("new-content", File.ReadAllText(dest));
            Assert.False(File.Exists(temp));
        }
        finally
        {
            TryDelete(dest);
            TryDelete(temp);
        }
    }

    [Fact]
    public async Task JsonSchemaVersion_Remains1_0_WithOptionalSectionsEmpty()
    {
        var service = new ReportExportService(NullLogger<ReportExportService>.Instance);
        var path = TempPath(".json");
        try
        {
            var result = await service.ExportAsync(new ReportExportOptions
            {
                ScanResult = CreateScanWithStats(),
                Format = ReportFormat.Json,
                DestinationPath = path,
                IncludeCategories = false,
                IncludeExtensions = false,
                IncludeErrors = false,
                ScanSessionId = Guid.NewGuid()
            });

            Assert.True(result.Succeeded);
            await using var stream = File.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(stream);
            Assert.Equal("1.0", doc.RootElement.GetProperty("schemaVersion").GetString());
            Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("categories").ValueKind);
            Assert.Equal(0, doc.RootElement.GetProperty("categories").GetArrayLength());
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task StaleSession_AfterWrite_CleansTempAndKeepsDestination()
    {
        var path = TempPath(".html");
        await File.WriteAllTextAsync(path, "previous-report");
        var service = new ReportExportService(NullLogger<ReportExportService>.Instance);
        var session = Guid.NewGuid();
        var calls = 0;

        var result = await service.ExportAsync(
            new ReportExportOptions
            {
                ScanResult = CreateScanWithStats(),
                Format = ReportFormat.Html,
                DestinationPath = path,
                ScanSessionId = session
            },
            sessionStillValid: _ =>
            {
                // First check (before build) succeeds; second (after write) fails.
                calls++;
                return calls == 1;
            });

        Assert.True(result.WasStaleSession);
        Assert.Equal("previous-report", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*.tmp"));
        TryDelete(path);
    }

    private static ScanResult CreateScanWithStats()
    {
        var root = Dir("root", MakeFile("a.bin", 100), Dir("sub", MakeFile("b.bin", 50)));
        return new ScanResult
        {
            RootNode = root,
            TotalFiles = 2,
            TotalDirectories = 2,
            TotalBytes = 150,
            Duration = TimeSpan.FromSeconds(1),
            FinalStatus = ScanPhase.Completed,
            LargestFiles =
            [
                new LargestFileInfo
                {
                    Name = "a.bin",
                    FullPath = root.Children[0].FullPath,
                    SizeBytes = 100,
                    Extension = ".bin"
                }
            ],
            CategoryStatistics =
            [
                new FileCategoryStatistics
                {
                    Category = FileCategory.Other,
                    FileCount = 2,
                    TotalSizeBytes = 150,
                    PercentageOfTotalBytes = 100
                }
            ],
            ExtensionStatistics =
            [
                new FileExtensionStatistics
                {
                    Extension = ".bin",
                    Category = FileCategory.Other,
                    FileCount = 2,
                    TotalSizeBytes = 150,
                    PercentageOfTotalBytes = 100
                }
            ],
            Errors =
            [
                new ScanError
                {
                    Path = @"C:\root\locked",
                    ErrorType = ScanErrorType.AccessDenied,
                    Message = "denied"
                }
            ]
        };
    }

    private static string TempPath(string ext) =>
        Path.Combine(Path.GetTempPath(), "ep-stab-" + Guid.NewGuid().ToString("N") + ext);

    private static FileSystemNode Dir(string name, params FileSystemNode[] children)
    {
        var node = new FileSystemNode
        {
            Name = name,
            FullPath = @"C:\tests\" + name,
            IsDirectory = true
        };

        foreach (var child in children)
        {
            var attached = child.IsDirectory
                ? RelocateDirectory(child, node.FullPath)
                : RelocateFile(child, node.FullPath);
            attached.Parent = node;
            node.Children.Add(attached);
            node.SizeBytes += attached.SizeBytes;
            node.FileCount += attached.IsDirectory ? attached.FileCount : 1;
            node.DirectoryCount += attached.IsDirectory ? 1 + attached.DirectoryCount : 0;
        }

        return node;
    }

    private static FileSystemNode RelocateFile(FileSystemNode source, string parentPath) =>
        new()
        {
            Name = source.Name,
            FullPath = Path.Combine(parentPath, source.Name),
            IsDirectory = false,
            SizeBytes = source.SizeBytes,
            FileCount = 1
        };

    private static FileSystemNode RelocateDirectory(FileSystemNode source, string parentPath)
    {
        var relocated = new FileSystemNode
        {
            Name = source.Name,
            FullPath = Path.Combine(parentPath, source.Name),
            IsDirectory = true,
            SizeBytes = source.SizeBytes,
            FileCount = source.FileCount,
            DirectoryCount = source.DirectoryCount
        };

        foreach (var child in source.Children)
        {
            var attached = child.IsDirectory
                ? RelocateDirectory(child, relocated.FullPath)
                : RelocateFile(child, relocated.FullPath);
            attached.Parent = relocated;
            relocated.Children.Add(attached);
        }

        return relocated;
    }

    private static FileSystemNode MakeFile(string name, long size) =>
        new()
        {
            Name = name,
            FullPath = @"C:\tests\" + name,
            IsDirectory = false,
            SizeBytes = size,
            FileCount = 1
        };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    private sealed class StubLocalization : ILocalizationService
    {
        public AppLanguageMode Preference => AppLanguageMode.English;

        public AppLanguageMode EffectiveLanguage => AppLanguageMode.English;

        public System.Globalization.CultureInfo CurrentCulture { get; } =
            System.Globalization.CultureInfo.GetCultureInfo("en-US");

        public IReadOnlyList<LanguageOption> AvailableLanguages { get; } = [];

        public event EventHandler? LanguageChanged
        {
            add { }
            remove { }
        }

        public void Initialize()
        {
        }

        public void SetPreference(AppLanguageMode mode)
        {
        }

        public void SwitchToTurkish()
        {
        }

        public string GetString(string key) => key;

        public string GetString(string key, params object[] args) =>
            string.Format(CurrentCulture, key, args);

        public bool HasKey(string key) => true;

        public IReadOnlyCollection<string> GetAllKeys() => [];

        public void Dispose()
        {
        }
    }
}
