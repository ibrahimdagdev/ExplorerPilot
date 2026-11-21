using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Scanning;
using ExplorerPilot.Infrastructure.Scanning;

namespace ExplorerPilot.Tests;

public sealed class FileTypeStatisticsTests
{
    [Theory]
    [InlineData("photo.JPG", ".jpg")]
    [InlineData("photo.jpg", ".jpg")]
    [InlineData("photo.Jpg", ".jpg")]
    [InlineData(".MP4", ".mp4")]
    [InlineData("readme", "")]
    [InlineData("archive.tar.gz", ".gz")]
    public void NormalizeExtension_IsCaseInsensitiveAndConsistent(string input, string expected)
    {
        Assert.Equal(expected, FileCategoryMapper.NormalizeExtension(input));
    }

    [Fact]
    public void Tracker_GroupsCaseInsensitiveExtensions()
    {
        var tracker = new FileTypeStatisticsTracker();
        tracker.Offer("a.JPG", 100);
        tracker.Offer("b.jpg", 50);
        tracker.Offer("c.Jpg", 25);

        var stats = tracker.BuildExtensionStatistics();
        var jpg = Assert.Single(stats);
        Assert.Equal(".jpg", jpg.Extension);
        Assert.Equal(3, jpg.FileCount);
        Assert.Equal(175, jpg.TotalSizeBytes);
        Assert.Equal(100d, jpg.PercentageOfTotalBytes);
        Assert.Equal(175d / 3d, jpg.AverageSizeBytes, 5);
    }

    [Fact]
    public void Tracker_FilesWithoutExtension_UseEmptyKey()
    {
        var tracker = new FileTypeStatisticsTracker();
        tracker.Offer("LICENSE", 10);
        tracker.Offer("Makefile", 20);

        var stats = Assert.Single(tracker.BuildExtensionStatistics());
        Assert.Equal(string.Empty, stats.Extension);
        Assert.Equal(FileCategory.Other, stats.Category);
        Assert.Equal(2, stats.FileCount);
        Assert.Equal(30, stats.TotalSizeBytes);
    }

    [Fact]
    public void Tracker_UnknownExtensions_MapToOther()
    {
        var tracker = new FileTypeStatisticsTracker();
        tracker.Offer("data.xyz", 40);

        var stats = Assert.Single(tracker.BuildExtensionStatistics());
        Assert.Equal(".xyz", stats.Extension);
        Assert.Equal(FileCategory.Other, stats.Category);
    }

    [Fact]
    public void Tracker_ComputesCountsBytesAverageAndPercentage()
    {
        var tracker = new FileTypeStatisticsTracker();
        tracker.Offer("a.mp4", 300);
        tracker.Offer("b.mp4", 100);
        tracker.Offer("c.txt", 100);

        var extensions = tracker.BuildExtensionStatistics();
        Assert.Equal(2, extensions.Count);

        var mp4 = extensions[0];
        Assert.Equal(".mp4", mp4.Extension);
        Assert.Equal(2, mp4.FileCount);
        Assert.Equal(400, mp4.TotalSizeBytes);
        Assert.Equal(80d, mp4.PercentageOfTotalBytes, 5);
        Assert.Equal(200d, mp4.AverageSizeBytes, 5);

        var txt = extensions[1];
        Assert.Equal(".txt", txt.Extension);
        Assert.Equal(1, txt.FileCount);
        Assert.Equal(100, txt.TotalSizeBytes);
        Assert.Equal(20d, txt.PercentageOfTotalBytes, 5);
        Assert.Equal(100d, txt.AverageSizeBytes, 5);

        Assert.Equal(3, tracker.TotalFiles);
        Assert.Equal(500, tracker.TotalBytes);
        Assert.Equal(2, tracker.UniqueExtensionCount);
    }

    [Fact]
    public void Tracker_CategoryMapping_AggregatesByCategory()
    {
        var tracker = new FileTypeStatisticsTracker();
        tracker.Offer("movie.mkv", 500);
        tracker.Offer("clip.mp4", 300);
        tracker.Offer("shot.png", 50);
        tracker.Offer("song.mp3", 40);
        tracker.Offer("notes.docx", 10);
        tracker.Offer("pack.zip", 20);
        tracker.Offer("app.exe", 30);
        tracker.Offer("weird.xyz", 5);

        var categories = tracker.BuildCategoryStatistics()
            .Where(c => c.FileCount > 0)
            .ToDictionary(c => c.Category);

        Assert.Equal(2, categories[FileCategory.Videos].FileCount);
        Assert.Equal(800, categories[FileCategory.Videos].TotalSizeBytes);
        Assert.Equal(1, categories[FileCategory.Images].FileCount);
        Assert.Equal(1, categories[FileCategory.Audio].FileCount);
        Assert.Equal(1, categories[FileCategory.Documents].FileCount);
        Assert.Equal(1, categories[FileCategory.Archives].FileCount);
        Assert.Equal(1, categories[FileCategory.Applications].FileCount);
        Assert.Equal(1, categories[FileCategory.Other].FileCount);

        var total = categories.Values.Sum(c => c.TotalSizeBytes);
        Assert.Equal(tracker.TotalBytes, total);
        Assert.Equal(100d, categories.Values.Sum(c => c.PercentageOfTotalBytes), 5);
    }

    [Fact]
    public void Tracker_EmptyScan_ReturnsEmptyStatistics()
    {
        var tracker = new FileTypeStatisticsTracker();
        Assert.Empty(tracker.BuildExtensionStatistics());
        Assert.All(tracker.BuildCategoryStatistics(), c =>
        {
            Assert.Equal(0, c.FileCount);
            Assert.Equal(0, c.TotalSizeBytes);
            Assert.Equal(0d, c.PercentageOfTotalBytes);
        });
    }

    [Fact]
    public void CategoryMapper_KnownExtensions()
    {
        Assert.Equal(FileCategory.Videos, FileCategoryMapper.GetCategory(".mp4"));
        Assert.Equal(FileCategory.Images, FileCategoryMapper.GetCategory(".png"));
        Assert.Equal(FileCategory.Audio, FileCategoryMapper.GetCategory(".flac"));
        Assert.Equal(FileCategory.Documents, FileCategoryMapper.GetCategory(".pdf"));
        Assert.Equal(FileCategory.Archives, FileCategoryMapper.GetCategory(".7z"));
        Assert.Equal(FileCategory.Applications, FileCategoryMapper.GetCategory(".dll"));
        Assert.Equal(FileCategory.Other, FileCategoryMapper.GetCategory(".unknown"));
        Assert.Equal(FileCategory.Other, FileCategoryMapper.GetCategory(""));
    }

    [Fact]
    public async Task ScanAsync_BuildsExtensionStatisticsWithoutSecondPass()
    {
        using var temp = TemporaryDirectory.Create();
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "a.MP4"), new byte[100]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "b.mp4"), new byte[50]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "c.TXT"), new byte[25]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "noext"), new byte[10]);

        var scanner = new DiskScanner();
        var result = await scanner.ScanAsync(temp.Path);

        Assert.Equal(ScanPhase.Completed, result.FinalStatus);
        Assert.Equal(4, result.TotalFiles);
        Assert.Equal(185, result.TotalBytes);

        Assert.Equal(result.TotalFiles, result.ExtensionStatistics.Sum(s => s.FileCount));
        Assert.Equal(result.TotalBytes, result.ExtensionStatistics.Sum(s => s.TotalSizeBytes));

        var mp4 = Assert.Single(result.ExtensionStatistics, s => s.Extension == ".mp4");
        Assert.Equal(2, mp4.FileCount);
        Assert.Equal(150, mp4.TotalSizeBytes);

        Assert.Contains(result.ExtensionStatistics, s => s.Extension == string.Empty && s.FileCount == 1);
        Assert.Equal(4, result.LargestFiles.Count);
    }

    [Fact]
    public async Task ScanAsync_PreservesLargestFilesWithFileTypeStats()
    {
        using var temp = TemporaryDirectory.Create();
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "small.bin"), new byte[10]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "large.bin"), new byte[500]);

        var result = await new DiskScanner().ScanAsync(temp.Path);

        Assert.Equal("large.bin", result.LargestFiles[0].Name);
        Assert.Equal(2, result.ExtensionStatistics.Sum(s => s.FileCount));
        Assert.Equal(FileCategory.Other, result.ExtensionStatistics.Single().Category);
    }

    [Fact]
    public async Task ScanAsync_Cancelled_MarksIncompleteAndKeepsPartialStats()
    {
        using var temp = TemporaryDirectory.Create();
        for (var i = 0; i < 60; i++)
        {
            var dir = Path.Combine(temp.Path, $"d{i:D3}");
            Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(Path.Combine(dir, $"f{i}.dat"), new byte[32]);
        }

        using var cts = new CancellationTokenSource();
        var progress = new Progress<Core.Models.ScanProgress>(_ =>
        {
            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
            }
        });

        var result = await new DiskScanner().ScanAsync(temp.Path, progress, cts.Token);

        Assert.Equal(ScanPhase.Cancelled, result.FinalStatus);
        Assert.False(result.IsComplete);
        Assert.True(result.ExtensionStatistics.Sum(s => s.FileCount) >= 0);
        Assert.True(result.LargestFiles.Count <= LargestFilesOptions.MaxTrackedFiles);
        Assert.Equal(
            result.ExtensionStatistics.Sum(s => s.TotalSizeBytes),
            result.CategoryStatistics.Sum(s => s.TotalSizeBytes));
        Assert.Equal(
            result.ExtensionStatistics.Sum(s => s.FileCount),
            result.CategoryStatistics.Sum(s => s.FileCount));
    }

    [Fact]
    public async Task ScanAsync_EmptyDirectory_HasEmptyFileTypeStats()
    {
        using var temp = TemporaryDirectory.Create();
        var result = await new DiskScanner().ScanAsync(temp.Path);

        Assert.Empty(result.ExtensionStatistics);
        Assert.All(result.CategoryStatistics, c => Assert.Equal(0, c.FileCount));
        Assert.Empty(result.LargestFiles);
    }

    [Fact]
    public void Tracker_DoesNotDoubleCount()
    {
        var tracker = new FileTypeStatisticsTracker();
        tracker.Offer("one.pdf", 100);
        tracker.Offer("two.pdf", 200);

        Assert.Equal(2, tracker.TotalFiles);
        Assert.Equal(300, tracker.TotalBytes);
        Assert.Equal(2, tracker.BuildExtensionStatistics().Single().FileCount);
    }
}
