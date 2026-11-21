using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Scanning;

namespace ExplorerPilot.Tests;

public sealed class LargestFilesTrackerTests
{
    [Fact]
    public void Offer_RanksDescendingBySize()
    {
        var tracker = new LargestFilesTracker(10);
        tracker.Offer(File("c.txt", 30));
        tracker.Offer(File("a.txt", 100));
        tracker.Offer(File("b.txt", 50));

        Assert.Equal(new[] { 100L, 50L, 30L }, tracker.ToSortedList().Select(f => f.SizeBytes).ToArray());
        Assert.Equal(new[] { "a.txt", "b.txt", "c.txt" }, tracker.ToSortedList().Select(f => f.Name).ToArray());
    }

    [Fact]
    public void Offer_RespectsCapacity_KeepsLargest()
    {
        var tracker = new LargestFilesTracker(3);
        for (var i = 1; i <= 10; i++)
        {
            tracker.Offer(File($"f{i}.bin", i * 10));
        }

        var sorted = tracker.ToSortedList();
        Assert.Equal(3, sorted.Count);
        Assert.Equal(new[] { 100L, 90L, 80L }, sorted.Select(f => f.SizeBytes).ToArray());
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    public void Offer_SupportsConfiguredDisplayLimits(int limit)
    {
        var tracker = new LargestFilesTracker(LargestFilesOptions.MaxTrackedFiles);
        for (var i = 0; i < LargestFilesOptions.MaxTrackedFiles + 250; i++)
        {
            tracker.Offer(File($"n{i}.dat", i + 1));
        }

        Assert.Equal(LargestFilesOptions.MaxTrackedFiles, tracker.Count);
        var displayed = tracker.ToSortedList().Take(limit).ToList();
        Assert.Equal(limit, displayed.Count);
        Assert.True(displayed[0].SizeBytes >= displayed[^1].SizeBytes);
        Assert.Equal(LargestFilesOptions.MaxTrackedFiles + 250, displayed[0].SizeBytes);
    }

    [Fact]
    public void Offer_EqualSizedFiles_AllKeptWhenUnderCapacity()
    {
        var tracker = new LargestFilesTracker(5);
        tracker.Offer(File("a.bin", 50));
        tracker.Offer(File("b.bin", 50));
        tracker.Offer(File("c.bin", 50));

        var sorted = tracker.ToSortedList();
        Assert.Equal(3, sorted.Count);
        Assert.All(sorted, f => Assert.Equal(50, f.SizeBytes));
    }

    [Fact]
    public void Offer_EqualToSmallestWhenFull_DoesNotGrow()
    {
        var tracker = new LargestFilesTracker(2);
        tracker.Offer(File("big.bin", 100));
        tracker.Offer(File("mid.bin", 50));
        tracker.Offer(File("also-mid.bin", 50));

        Assert.Equal(2, tracker.Count);
        Assert.DoesNotContain(tracker.ToSortedList(), f => f.Name == "also-mid.bin");
    }

    [Fact]
    public void Offer_IgnoresFilesSmallerThanThresholdWhenFull()
    {
        var tracker = new LargestFilesTracker(2);
        tracker.Offer(File("a.bin", 100));
        tracker.Offer(File("b.bin", 90));
        tracker.Offer(File("tiny.bin", 1));

        Assert.Equal(2, tracker.Count);
        Assert.DoesNotContain(tracker.ToSortedList(), f => f.Name == "tiny.bin");
    }

    [Fact]
    public void Offer_EmptyTracker_ReturnsEmptyList()
    {
        var tracker = new LargestFilesTracker(10);
        Assert.Empty(tracker.ToSortedList());
    }

    [Fact]
    public void Offer_CapturesExtensionAndTurkishPath()
    {
        var tracker = new LargestFilesTracker(5);
        tracker.Offer("rapor-öğrenci.txt", @"C:\Veriler\rapor-öğrenci.txt", 2048, DateTimeOffset.UtcNow);

        var file = Assert.Single(tracker.ToSortedList());
        Assert.Equal(".txt", file.Extension);
        Assert.Contains("öğrenci", file.FullPath, StringComparison.Ordinal);
        Assert.Equal(2048, file.SizeBytes);
    }

    [Fact]
    public void Constructor_RejectsInvalidCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LargestFilesTracker(0));
    }

    private static LargestFileInfo File(string name, long size) =>
        new()
        {
            Name = name,
            FullPath = @"C:\scan\" + name,
            SizeBytes = size,
            Extension = Path.GetExtension(name),
            LastModifiedUtc = DateTimeOffset.UtcNow
        };
}
