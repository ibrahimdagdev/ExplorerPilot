using System.Text;
using System.Text.Json;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Reporting;
using ExplorerPilot.Infrastructure.Reporting;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

public sealed class ReportExportTests
{
    [Theory]
    [InlineData("=CMD()", "'=CMD()")]
    [InlineData("+1234", "'+1234")]
    [InlineData("-total", "'-total")]
    [InlineData("@sum", "'@sum")]
    [InlineData("safe", "safe")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    public void CsvEscaping_ProtectsFormulasAndQuotes(string input, string expected)
    {
        Assert.Equal(expected, CsvReportEscaping.Escape(input));
    }

    [Fact]
    public void HtmlEscaping_EncodesUserText()
    {
        var raw = "<script>alert('x')</script> & \"y\"";
        var escaped = HtmlReportEscaping.Escape(raw);
        Assert.DoesNotContain("<script>", escaped, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", escaped, StringComparison.Ordinal);
        Assert.Contains("&amp;", escaped, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_MarksCancelledScanAsIncomplete()
    {
        var scan = CreateScan(ScanPhase.Cancelled, includeError: true);
        var report = DiskAnalysisReportBuilder.Build(scan, includeDirectoryTree: false, maxLargestFiles: 100, maxLargestFolders: 100);

        Assert.Equal(ReportCompleteness.Cancelled, report.Summary.Completeness);
        Assert.True(report.Summary.IsIncomplete);
        Assert.Equal(DiskAnalysisReport.CurrentSchemaVersion, report.SchemaVersion);
        Assert.True(report.LargestFiles.Count <= 100);
        Assert.True(report.LargestFolders.Count <= 100);
    }

    [Fact]
    public void Builder_IncludesDirectoryTree_WhenRequested()
    {
        var scan = CreateScan(ScanPhase.Completed);
        var withTree = DiskAnalysisReportBuilder.Build(scan, includeDirectoryTree: true);
        var withoutTree = DiskAnalysisReportBuilder.Build(scan, includeDirectoryTree: false);

        Assert.NotNull(withTree.DirectoryTree);
        Assert.Equal(scan.RootNode.Name, withTree.DirectoryTree!.Name);
        Assert.Null(withoutTree.DirectoryTree);
    }

    [Fact]
    public void Builder_HandlesEmptyFolderAndAccessErrors()
    {
        var empty = new FileSystemNode
        {
            Name = "empty",
            FullPath = @"C:\empty",
            IsDirectory = true,
            SizeBytes = 0
        };
        var scan = new ScanResult
        {
            RootNode = empty,
            TotalFiles = 0,
            TotalDirectories = 1,
            TotalBytes = 0,
            Duration = TimeSpan.FromSeconds(1),
            FinalStatus = ScanPhase.Completed,
            Errors =
            [
                new ScanError
                {
                    Path = @"C:\empty\secret",
                    ErrorType = ScanErrorType.AccessDenied,
                    Message = "Access denied"
                }
            ]
        };

        var report = DiskAnalysisReportBuilder.Build(scan, includeDirectoryTree: true);
        Assert.Equal(ReportCompleteness.Warnings, report.Summary.Completeness);
        Assert.Single(report.Errors);
        Assert.Equal("AccessDenied", report.Errors[0].ErrorTypeCode);
        Assert.NotNull(report.DirectoryTree);
        Assert.Empty(report.DirectoryTree!.Children);
    }

    [Fact]
    public async Task JsonExport_IsVersionedCultureInvariantAndRoundTrippable()
    {
        var service = new ReportExportService(NullLogger<ReportExportService>.Instance);
        var path = Path.Combine(Path.GetTempPath(), "ep-report-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var scan = CreateScan(ScanPhase.Completed);
            var result = await service.ExportAsync(new ReportExportOptions
            {
                ScanResult = scan,
                Format = ReportFormat.Json,
                DestinationPath = path,
                IncludeDirectoryTree = false,
                ScanSessionId = Guid.NewGuid()
            });

            Assert.True(result.Succeeded);
            Assert.True(File.Exists(path));

            await using var stream = File.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;
            Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
            Assert.True(root.GetProperty("summary").GetProperty("totalBytes").ValueKind == JsonValueKind.Number);
            Assert.Contains("T", root.GetProperty("generatedAtUtc").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task HtmlExport_IsOfflineAndEscapesContent()
    {
        var service = new ReportExportService(NullLogger<ReportExportService>.Instance);
        var path = Path.Combine(Path.GetTempPath(), "ep-report-" + Guid.NewGuid().ToString("N") + ".html");
        try
        {
            var root = Dir("root<script>", MakeFile("evil&=.txt", 10));
            var scan = new ScanResult
            {
                RootNode = root,
                TotalFiles = 1,
                TotalDirectories = 1,
                TotalBytes = 10,
                Duration = TimeSpan.FromMilliseconds(5),
                FinalStatus = ScanPhase.Completed,
                LargestFiles =
                [
                    new LargestFileInfo
                    {
                        Name = "evil&=.txt",
                        FullPath = root.Children[0].FullPath,
                        SizeBytes = 10,
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
            var html = await File.ReadAllTextAsync(path);
            Assert.Contains("<!DOCTYPE html>", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("cdn.", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<script src=", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("prefers-color-scheme", html, StringComparison.Ordinal);
            Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
            Assert.Contains("&amp;", html, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task CsvExport_WritesUtf8BomAndEscapesInjection()
    {
        var service = new ReportExportService(NullLogger<ReportExportService>.Instance);
        var path = Path.Combine(Path.GetTempPath(), "ep-report-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var root = Dir("root", MakeFile("=1+1.txt", 20));
            var scan = new ScanResult
            {
                RootNode = root,
                TotalFiles = 1,
                TotalDirectories = 1,
                TotalBytes = 20,
                Duration = TimeSpan.FromSeconds(1),
                FinalStatus = ScanPhase.Completed,
                LargestFiles =
                [
                    new LargestFileInfo
                    {
                        Name = "=1+1.txt",
                        FullPath = root.Children[0].FullPath,
                        SizeBytes = 20,
                        Extension = ".txt"
                    }
                ]
            };

            var result = await service.ExportAsync(new ReportExportOptions
            {
                ScanResult = scan,
                Format = ReportFormat.Csv,
                DestinationPath = path,
                ScanSessionId = Guid.NewGuid()
            });

            Assert.True(result.Succeeded);
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.True(bytes.Length >= 3);
            Assert.Equal(0xEF, bytes[0]);
            Assert.Equal(0xBB, bytes[1]);
            Assert.Equal(0xBF, bytes[2]);

            var text = Encoding.UTF8.GetString(bytes);
            Assert.Contains("'=1+1.txt", text, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public async Task Cancellation_RemovesTempAndLeavesNoDestination()
    {
        var service = new ReportExportService(NullLogger<ReportExportService>.Instance);
        var path = Path.Combine(Path.GetTempPath(), "ep-report-" + Guid.NewGuid().ToString("N") + ".json");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.ExportAsync(
            new ReportExportOptions
            {
                ScanResult = CreateScan(ScanPhase.Completed, largeFanout: 2_000),
                Format = ReportFormat.Json,
                DestinationPath = path,
                IncludeDirectoryTree = true,
                ScanSessionId = Guid.NewGuid()
            },
            cancellationToken: cts.Token);

        Assert.True(result.WasCancelled);
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + "*.tmp"));
    }

    [Fact]
    public async Task StaleSession_DoesNotLeaveDestination()
    {
        var service = new ReportExportService(NullLogger<ReportExportService>.Instance);
        var path = Path.Combine(Path.GetTempPath(), "ep-report-" + Guid.NewGuid().ToString("N") + ".html");
        var session = Guid.NewGuid();

        var result = await service.ExportAsync(
            new ReportExportOptions
            {
                ScanResult = CreateScan(ScanPhase.Completed),
                Format = ReportFormat.Html,
                DestinationPath = path,
                ScanSessionId = session
            },
            sessionStillValid: _ => false);

        Assert.True(result.WasStaleSession);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task LargeDataset_ExportsWithinBudget()
    {
        var service = new ReportExportService(NullLogger<ReportExportService>.Instance);
        var path = Path.Combine(Path.GetTempPath(), "ep-report-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await service.ExportAsync(new ReportExportOptions
            {
                ScanResult = CreateScan(ScanPhase.Completed, largeFanout: 5_000),
                Format = ReportFormat.Csv,
                DestinationPath = path,
                IncludeDirectoryTree = false,
                MaxLargestFiles = 100,
                MaxLargestFolders = 100,
                ScanSessionId = Guid.NewGuid()
            });
            sw.Stop();

            Assert.True(result.Succeeded);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15));
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static ScanResult CreateScan(ScanPhase status, bool includeError = false, int largeFanout = 0)
    {
        var children = new List<FileSystemNode>();
        if (largeFanout > 0)
        {
            for (var i = 0; i < largeFanout; i++)
            {
                children.Add(MakeFile($"f{i}.bin", 100 + i));
            }
        }
        else
        {
            children.Add(MakeFile("a.bin", 1_000));
            children.Add(Dir("sub", MakeFile("b.bin", 500)));
        }

        var root = Dir("root", children.ToArray());
        var largest = root.Children
            .Where(c => !c.IsDirectory)
            .Concat(root.Children.Where(c => c.IsDirectory).SelectMany(c => c.Children))
            .Where(c => !c.IsDirectory)
            .OrderByDescending(c => c.SizeBytes)
            .Take(100)
            .Select(c => new LargestFileInfo
            {
                Name = c.Name,
                FullPath = c.FullPath,
                SizeBytes = c.SizeBytes,
                Extension = Path.GetExtension(c.Name)
            })
            .ToList();

        return new ScanResult
        {
            RootNode = root,
            TotalFiles = root.FileCount,
            TotalDirectories = root.DirectoryCount + 1,
            TotalBytes = root.SizeBytes,
            Duration = TimeSpan.FromSeconds(2.5),
            FinalStatus = status,
            LargestFiles = largest,
            ExtensionStatistics =
            [
                new FileExtensionStatistics
                {
                    Extension = ".bin",
                    Category = FileCategory.Other,
                    FileCount = Math.Max(1, root.FileCount),
                    TotalSizeBytes = root.SizeBytes,
                    PercentageOfTotalBytes = 100
                }
            ],
            CategoryStatistics =
            [
                new FileCategoryStatistics
                {
                    Category = FileCategory.Other,
                    FileCount = Math.Max(1, root.FileCount),
                    TotalSizeBytes = root.SizeBytes,
                    PercentageOfTotalBytes = 100
                }
            ],
            Errors = includeError
                ?
                [
                    new ScanError
                    {
                        Path = @"C:\root\locked",
                        ErrorType = ScanErrorType.AccessDenied,
                        Message = "denied"
                    }
                ]
                : []
        };
    }

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
}
