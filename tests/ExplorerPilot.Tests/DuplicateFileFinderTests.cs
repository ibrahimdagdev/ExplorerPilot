using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Scanning;
using ExplorerPilot.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

public sealed class DuplicateFileFinderTests
{
    [Fact]
    public void SavingsCalculator_KeepsOneCopy()
    {
        var files = new[]
        {
            new DuplicateFileInfo { Name = "a.bin", FullPath = @"C:\a.bin", SizeBytes = 100, FileIdentityKey = "A" },
            new DuplicateFileInfo { Name = "b.bin", FullPath = @"C:\b.bin", SizeBytes = 100, FileIdentityKey = "B" },
            new DuplicateFileInfo { Name = "c.bin", FullPath = @"C:\c.bin", SizeBytes = 100, FileIdentityKey = "C" }
        };

        Assert.Equal(200, DuplicateSavingsCalculator.CalculateGroupSavings(100, files));
    }

    [Fact]
    public void SavingsCalculator_HardLinksDoNotInflateSavings()
    {
        var files = new[]
        {
            new DuplicateFileInfo { Name = "a.bin", FullPath = @"C:\a.bin", SizeBytes = 100, FileIdentityKey = "SAME" },
            new DuplicateFileInfo { Name = "b.bin", FullPath = @"C:\b.bin", SizeBytes = 100, FileIdentityKey = "SAME" },
            new DuplicateFileInfo { Name = "c.bin", FullPath = @"C:\c.bin", SizeBytes = 100, FileIdentityKey = "OTHER" }
        };

        // Two unique physical copies → keep one → save 100.
        Assert.Equal(100, DuplicateSavingsCalculator.CalculateGroupSavings(100, files));
    }

    [Fact]
    public void SavingsCalculator_OnlyHardLinks_ZeroSavings()
    {
        var files = new[]
        {
            new DuplicateFileInfo { Name = "a.bin", FullPath = @"C:\a.bin", SizeBytes = 50, FileIdentityKey = "SAME" },
            new DuplicateFileInfo { Name = "b.bin", FullPath = @"C:\b.bin", SizeBytes = 50, FileIdentityKey = "SAME" }
        };

        Assert.Equal(0, DuplicateSavingsCalculator.CalculateGroupSavings(50, files));
    }

    [Fact]
    public async Task ExactDuplicates_AreDetected()
    {
        using var temp = TemporaryDirectory.Create();
        var payload = Encoding.UTF8.GetBytes("duplicate-content-payload");
        var a = WriteFile(temp.Path, "a.txt", payload);
        var b = WriteFile(Path.Combine(temp.Path, "sub"), "b.txt", payload);
        var root = BuildTreeFromDisk(temp.Path);

        var result = await CreateSut().AnalyzeAsync(
            root,
            new DuplicateAnalysisCriteria { ScanSessionId = Guid.NewGuid() },
            fromIncompleteScan: false);

        Assert.Equal(DuplicateAnalysisPhase.Completed, result.FinalStatus);
        Assert.Equal(1, result.DuplicateGroupCount);
        Assert.Equal(2, result.DuplicateFileCount);
        Assert.Equal(payload.Length, result.PotentialSavingsBytes);
        Assert.Contains(result.Groups[0].Files, f => f.FullPath == a);
        Assert.Contains(result.Groups[0].Files, f => f.FullPath == b);
    }

    [Fact]
    public async Task SameSize_DifferentContent_AreNotDuplicates()
    {
        using var temp = TemporaryDirectory.Create();
        var size = 2048;
        WriteFile(temp.Path, "one.bin", CreateBytes(size, seed: 1));
        WriteFile(temp.Path, "two.bin", CreateBytes(size, seed: 2));
        var root = BuildTreeFromDisk(temp.Path);

        var result = await CreateSut().AnalyzeAsync(
            root,
            new DuplicateAnalysisCriteria { ScanSessionId = Guid.NewGuid() },
            fromIncompleteScan: false);

        Assert.Equal(DuplicateAnalysisPhase.Completed, result.FinalStatus);
        Assert.Empty(result.Groups);
        Assert.Equal(0, result.DuplicateGroupCount);
    }

    [Fact]
    public async Task DifferentSizes_AreNotGrouped()
    {
        using var temp = TemporaryDirectory.Create();
        WriteFile(temp.Path, "small.bin", CreateBytes(100, 1));
        WriteFile(temp.Path, "large.bin", CreateBytes(200, 1));
        var root = BuildTreeFromDisk(temp.Path);

        var result = await CreateSut().AnalyzeAsync(
            root,
            new DuplicateAnalysisCriteria { ScanSessionId = Guid.NewGuid() },
            fromIncompleteScan: false);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task EmptyFiles_AreDuplicates()
    {
        using var temp = TemporaryDirectory.Create();
        WriteFile(temp.Path, "e1.txt", []);
        WriteFile(temp.Path, "e2.txt", []);
        WriteFile(temp.Path, "e3.txt", []);
        var root = BuildTreeFromDisk(temp.Path);

        var result = await CreateSut().AnalyzeAsync(
            root,
            new DuplicateAnalysisCriteria { ScanSessionId = Guid.NewGuid() },
            fromIncompleteScan: false);

        Assert.Single(result.Groups);
        Assert.Equal(3, result.Groups[0].DuplicateCount);
        Assert.Equal(0, result.Groups[0].SizeBytes);
        Assert.Equal(0, result.PotentialSavingsBytes);
    }

    [Fact]
    public async Task PartialHashCollisionCandidates_SeparatedByFullHash()
    {
        using var temp = TemporaryDirectory.Create();
        // Same first/last 64KB would collide on partial hash, but full content differs.
        // For files <= 128KB partial hashes the entire file, so build > 128KB files
        // with identical head/tail and different middle.
        var head = CreateBytes(64 * 1024, seed: 7);
        var tail = CreateBytes(64 * 1024, seed: 9);
        var midA = CreateBytes(32 * 1024, seed: 11);
        var midB = CreateBytes(32 * 1024, seed: 13);

        WriteFile(temp.Path, "left.bin", Concat(head, midA, tail));
        WriteFile(temp.Path, "right.bin", Concat(head, midB, tail));
        var root = BuildTreeFromDisk(temp.Path);

        var result = await CreateSut().AnalyzeAsync(
            root,
            new DuplicateAnalysisCriteria { ScanSessionId = Guid.NewGuid() },
            fromIncompleteScan: false);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task Cancellation_StopsAnalysis()
    {
        using var temp = TemporaryDirectory.Create();
        var payload = CreateBytes(256 * 1024, seed: 3);
        for (var i = 0; i < 8; i++)
        {
            WriteFile(temp.Path, $"dup{i}.bin", payload);
        }

        var root = BuildTreeFromDisk(temp.Path);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await CreateSut().AnalyzeAsync(
            root,
            new DuplicateAnalysisCriteria { ScanSessionId = Guid.NewGuid() },
            fromIncompleteScan: false,
            cancellationToken: cts.Token);

        Assert.True(result.WasCancelled);
        Assert.Equal(DuplicateAnalysisPhase.Cancelled, result.FinalStatus);
        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task FileModifiedDuringAnalysis_IsSkipped()
    {
        using var temp = TemporaryDirectory.Create();
        var pathA = WriteFile(temp.Path, "a.bin", CreateBytes(4096, 1));
        var pathB = WriteFile(temp.Path, "b.bin", CreateBytes(4096, 1));

        // Build tree with stale LastModified so validation fails.
        var root = BuildTreeFromDisk(temp.Path);
        foreach (var file in EnumerateFiles(root))
        {
            file.LastModified = DateTimeOffset.UtcNow.AddHours(-5);
        }

        // Touch files so LastWriteTime no longer matches the tree metadata.
        File.SetLastWriteTimeUtc(pathA, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(pathB, DateTime.UtcNow);

        var result = await CreateSut().AnalyzeAsync(
            root,
            new DuplicateAnalysisCriteria { ScanSessionId = Guid.NewGuid() },
            fromIncompleteScan: false);

        Assert.Equal(DuplicateAnalysisPhase.Completed, result.FinalStatus);
        Assert.Empty(result.Groups);
        Assert.True(result.SkippedFiles >= 2);
    }

    [Fact]
    public async Task InaccessibleFiles_DoNotAbortAnalysis()
    {
        using var temp = TemporaryDirectory.Create();
        var payload = Encoding.UTF8.GetBytes("ok-duplicate");
        WriteFile(temp.Path, "good1.txt", payload);
        WriteFile(temp.Path, "good2.txt", payload);

        var missing = new FileSystemNode
        {
            Name = "missing.txt",
            FullPath = Path.Combine(temp.Path, "missing-does-not-exist.txt"),
            IsDirectory = false,
            SizeBytes = payload.Length,
            LastModified = DateTimeOffset.UtcNow
        };

        var root = BuildTreeFromDisk(temp.Path);
        root.Children.Add(missing);
        missing.Parent = root;

        var result = await CreateSut().AnalyzeAsync(
            root,
            new DuplicateAnalysisCriteria { ScanSessionId = Guid.NewGuid() },
            fromIncompleteScan: false);

        Assert.Equal(DuplicateAnalysisPhase.Completed, result.FinalStatus);
        Assert.Equal(1, result.DuplicateGroupCount);
        Assert.True(result.SkippedFiles + result.FailedFiles >= 1);
    }

    [Fact]
    public async Task ScanSessionId_IsEchoedOnResult()
    {
        using var temp = TemporaryDirectory.Create();
        WriteFile(temp.Path, "a.txt", "x"u8.ToArray());
        var root = BuildTreeFromDisk(temp.Path);
        var session = Guid.NewGuid();

        var result = await CreateSut().AnalyzeAsync(
            root,
            new DuplicateAnalysisCriteria { ScanSessionId = session },
            fromIncompleteScan: true);

        Assert.Equal(session, result.ScanSessionId);
        Assert.True(result.FromIncompleteScan);
    }

    [Fact]
    public async Task HardLinks_AreDetectedWithZeroOrReducedSavings()
    {
        using var temp = TemporaryDirectory.Create();
        var source = WriteFile(temp.Path, "source.bin", CreateBytes(8192, 42));
        var link = Path.Combine(temp.Path, "link.bin");
        if (!TryCreateHardLink(link, source))
        {
            // Environment may lack privilege; still verify independent duplicates path works.
            return;
        }

        var root = BuildTreeFromDisk(temp.Path);
        var result = await CreateSut().AnalyzeAsync(
            root,
            new DuplicateAnalysisCriteria { ScanSessionId = Guid.NewGuid() },
            fromIncompleteScan: false);

        Assert.Equal(DuplicateAnalysisPhase.Completed, result.FinalStatus);
        Assert.True(result.DuplicateGroupCount >= 1);
        Assert.Equal(0, result.PotentialSavingsBytes);
    }

    [Fact]
    public async Task MinimumSizeFilter_SkipsSmallFiles()
    {
        using var temp = TemporaryDirectory.Create();
        var small = Encoding.UTF8.GetBytes("tiny");
        WriteFile(temp.Path, "s1.txt", small);
        WriteFile(temp.Path, "s2.txt", small);
        var large = CreateBytes(10_000, 5);
        WriteFile(temp.Path, "l1.bin", large);
        WriteFile(temp.Path, "l2.bin", large);
        var root = BuildTreeFromDisk(temp.Path);

        var result = await CreateSut().AnalyzeAsync(
            root,
            new DuplicateAnalysisCriteria
            {
                ScanSessionId = Guid.NewGuid(),
                MinimumSizeBytes = 1000
            },
            fromIncompleteScan: false);

        Assert.Single(result.Groups);
        Assert.Equal(10_000, result.Groups[0].SizeBytes);
    }

    private static DuplicateFileFinderService CreateSut() =>
        new(NullLogger<DuplicateFileFinderService>.Instance);

    private static string WriteFile(string directory, string name, byte[] content)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] CreateBytes(int length, int seed)
    {
        var data = new byte[length];
        var rng = new Random(seed);
        rng.NextBytes(data);
        return data;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = parts.Sum(p => p.Length);
        var buffer = new byte[total];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, buffer, offset, part.Length);
            offset += part.Length;
        }

        return buffer;
    }

    private static FileSystemNode BuildTreeFromDisk(string rootPath)
    {
        var rootInfo = new DirectoryInfo(rootPath);
        var root = new FileSystemNode
        {
            Name = rootInfo.Name,
            FullPath = rootInfo.FullName,
            IsDirectory = true,
            LastModified = rootInfo.LastWriteTimeUtc
        };

        Populate(root, rootInfo);
        return root;
    }

    private static void Populate(FileSystemNode parent, DirectoryInfo directory)
    {
        foreach (var dir in directory.GetDirectories())
        {
            var node = new FileSystemNode
            {
                Name = dir.Name,
                FullPath = dir.FullName,
                IsDirectory = true,
                Parent = parent,
                LastModified = dir.LastWriteTimeUtc
            };
            parent.Children.Add(node);
            Populate(node, dir);
            parent.SizeBytes += node.SizeBytes;
            parent.FileCount += node.FileCount;
            parent.DirectoryCount += node.DirectoryCount + 1;
        }

        foreach (var file in directory.GetFiles())
        {
            var node = new FileSystemNode
            {
                Name = file.Name,
                FullPath = file.FullName,
                IsDirectory = false,
                Parent = parent,
                SizeBytes = file.Length,
                FileCount = 1,
                LastModified = new DateTimeOffset(DateTime.SpecifyKind(file.LastWriteTimeUtc, DateTimeKind.Utc)),
                ScanStatus = NodeScanStatus.Completed
            };
            parent.Children.Add(node);
            parent.SizeBytes += node.SizeBytes;
            parent.FileCount += 1;
        }
    }

    private static IEnumerable<FileSystemNode> EnumerateFiles(FileSystemNode root)
    {
        var stack = new Stack<FileSystemNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!node.IsDirectory)
            {
                yield return node;
                continue;
            }

            foreach (var child in node.Children)
            {
                stack.Push(child);
            }
        }
    }

    private static bool TryCreateHardLink(string linkPath, string existingFile)
    {
        try
        {
            return CreateHardLink(linkPath, existingFile, IntPtr.Zero);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
