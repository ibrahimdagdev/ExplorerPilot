using System.Diagnostics;
using System.IO;
using ExplorerPilot.App.ViewModels;
using ExplorerPilot.Infrastructure.Scanning;

// One-shot ScanCore vs ApplyResult (tree) measurement for release notes.
var root = args.Length > 0 ? args[0] : CreateFixture(10_000);
var keep = args.Length > 0;
Console.WriteLine($"Fixture: {root}");

try
{
    var scanner = new DiskScanner();
    var wall = Stopwatch.StartNew();
    var result = await scanner.ScanAsync(root);
    wall.Stop();

    var apply = Stopwatch.StartNew();
    var tree = new TreeNodeViewModel(result.RootNode, isRoot: true) { IsExpanded = true };
    apply.Stop();

    var expand = Stopwatch.StartNew();
    var firstDir = tree.Children.FirstOrDefault(c => c.IsDirectory);
    firstDir?.EnsureChildrenMaterialized();
    expand.Stop();

    Console.WriteLine($"FinalStatus={result.FinalStatus}");
    Console.WriteLine($"Files={result.TotalFiles} Dirs={result.TotalDirectories} Bytes={result.TotalBytes}");
    Console.WriteLine($"ScanCoreMs={(long)result.Duration.TotalMilliseconds}");
    Console.WriteLine($"WallScanMs={wall.ElapsedMilliseconds}");
    Console.WriteLine($"ApplyResultTreeMs={apply.ElapsedMilliseconds}");
    Console.WriteLine($"ExpandFirstBucketMs={expand.ElapsedMilliseconds}");
    Console.WriteLine($"RootChildren={tree.Children.Count}");
}
finally
{
    if (!keep && Directory.Exists(root))
    {
        try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
    }
}

static string CreateFixture(int files)
{
    var root = Path.Combine(Path.GetTempPath(), "ExplorerPilot-measure-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    for (var i = 0; i < files; i++)
    {
        var dir = Path.Combine(root, $"b{i / 100:D3}");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, $"f{i:D5}.bin"), [1]);
    }

    return root;
}
