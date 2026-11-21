using ExplorerPilot.App.ViewModels;
using ExplorerPilot.Core.Enums;
using ExplorerPilot.Infrastructure.Scanning;

namespace ExplorerPilot.Tests;

/// <summary>
/// Optimized scan → Explorer tree ApplyResult path on a real temp fixture.
/// Tab AttachScan coverage lives in dedicated module tests; this verifies DI-default scanner + lazy tree.
/// </summary>
public sealed class LiveScanModulesIntegrationTests
{
    [Fact]
    public async Task OptimizedScan_FeedsExplorerTreeAndSummaryAggregates()
    {
        using var temp = TemporaryDirectory.Create();
        Directory.CreateDirectory(Path.Combine(temp.Path, "docs"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "docs", "a.txt"), "hello");
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "big.bin"), new byte[2048]);
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "dup1.bin"), new byte[] { 1, 2, 3, 4 });
        await File.WriteAllBytesAsync(Path.Combine(temp.Path, "dup2.bin"), new byte[] { 1, 2, 3, 4 });
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "docs", "note.md"), "# note");

        var scanner = new DiskScanner();
        Assert.Equal(DiskScannerEnumerationMode.FileSystemInfoMetadata, scanner.EnumerationMode);

        var result = await scanner.ScanAsync(temp.Path);
        Assert.Equal(ScanPhase.Completed, result.FinalStatus);
        Assert.True(result.TotalFiles >= 5);
        Assert.True(result.TotalBytes >= 2048);
        Assert.NotEmpty(result.LargestFiles);
        Assert.Contains(result.LargestFiles, f => f.Name == "big.bin");
        Assert.NotEmpty(result.ExtensionStatistics);
        Assert.NotEmpty(result.CategoryStatistics);

        var tree = new TreeNodeViewModel(result.RootNode, isRoot: true) { IsExpanded = true };
        Assert.True(tree.AreChildrenMaterialized);
        Assert.Contains(tree.Children, c => c.Name is "big.bin" or "docs");

        var docs = tree.Children.First(c => c.Name == "docs");
        docs.IsExpanded = true;
        Assert.True(docs.AreChildrenMaterialized);
        Assert.Contains(docs.Children, c => c.Name == "a.txt");
    }
}
