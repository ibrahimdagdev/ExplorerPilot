using System.Globalization;
using ExplorerPilot.App.Localization;
using ExplorerPilot.App.Services;
using ExplorerPilot.App.ViewModels;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Treemap;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExplorerPilot.Tests;

/// <summary>
/// Integration/regression coverage for TreemapTabViewModel: selection sync, Other drill-down,
/// keyboard focus, tooltip cache, session lifecycle, and large fan-out layouts.
/// </summary>
public sealed class TreemapIntegrationTests
{
    [Fact]
    public async Task AttachScan_AppliesLayout_AndSelectFromExplorerHighlightsChild()
    {
        var (vm, root, deepFile) = CreateDeepTreeVm();
        var session = Guid.NewGuid();
        vm.AttachScan(root, session, incomplete: false);
        await WaitForLayoutAsync(vm);

        Assert.NotNull(vm.Layout);
        Assert.True(vm.Layout!.Rectangles.Count > 0);

        FileSystemNode? echoed = null;
        vm.NodeSelected += (_, n) => echoed = n;

        vm.SelectFromExplorer(deepFile, session);
        await WaitForAsync(() => vm.SelectedNode is not null && ReferenceEquals(vm.SelectedNode, deepFile));

        Assert.Same(deepFile, vm.SelectedNode);
        Assert.Null(echoed); // explorer→treemap must not bounce NodeSelected
        Assert.Equal(session, vm.ScanSessionId);
    }

    [Fact]
    public async Task SelectFromExplorer_RejectsStaleSession()
    {
        var (vm, root, deepFile) = CreateDeepTreeVm();
        var session = Guid.NewGuid();
        vm.AttachScan(root, session, incomplete: false);
        await WaitForLayoutAsync(vm);

        vm.SelectFromExplorer(deepFile, Guid.NewGuid());
        await Task.Delay(50);

        Assert.Null(vm.SelectedNode);
    }

    [Fact]
    public async Task TreemapSelection_RaisesNodeSelected_ForSyncToTreeView()
    {
        var (vm, root, _) = CreateDeepTreeVm();
        var session = Guid.NewGuid();
        vm.AttachScan(root, session, incomplete: false);
        await WaitForLayoutAsync(vm);

        var target = Assert.Single(vm.Layout!.Rectangles.Where(r => !r.IsAggregate && r.Source is not null).Take(1));
        FileSystemNode? selected = null;
        vm.NodeSelected += (_, n) => selected = n;

        vm.SelectRectangle(target);

        Assert.Same(target.Source, selected);
        Assert.Equal(target.Id, vm.SelectedRectangleId);
        Assert.Equal(target.Id, vm.FocusedRectangleId);
    }

    [Fact]
    public async Task OtherDrillDown_Back_Home_AndBreadcrumb_Work()
    {
        var (vm, root) = CreateFanoutVm(childCount: 40, tinySize: 10, bigSize: 1_000_000);
        var session = Guid.NewGuid();
        vm.AttachScan(root, session, incomplete: false);
        await WaitForLayoutAsync(vm);

        var other = Assert.Single(vm.Layout!.Rectangles, r => r.IsAggregate);
        Assert.True(other.AggregatedCount >= 2);

        vm.ActivateRectangle(other);
        await WaitForLayoutAsync(vm);

        Assert.True(vm.CanNavigateBack);
        Assert.True(vm.Breadcrumbs.Count >= 2);
        Assert.True(vm.Layout!.IsAggregateView);
        Assert.All(vm.Layout.Rectangles, r => Assert.False(r.IsAggregate));

        var crumbRoot = vm.Breadcrumbs[0];
        vm.NavigateBreadcrumbCommand.Execute(crumbRoot);
        await WaitForLayoutAsync(vm);
        Assert.False(vm.Layout!.IsAggregateView);

        vm.ActivateRectangle(other);
        await WaitForLayoutAsync(vm);
        vm.HomeCommand.Execute(null);
        await WaitForLayoutAsync(vm);

        Assert.Single(vm.Breadcrumbs);
        Assert.False(vm.CanNavigateBack);
        Assert.Same(root, vm.Layout!.ViewRoot);
    }

    [Fact]
    public async Task SingleMemberAggregate_NavigatesDirectoryOrHighlightsFile()
    {
        var loc = new StubLocalization();
        var vm = CreateVm(loc);
        var onlyDir = Dir("only-dir", File("inner.bin", 50));
        var parent = Dir("root", File("big.bin", 10_000), onlyDir);
        // Force a synthetic single-member aggregate drill path.
        var session = Guid.NewGuid();
        vm.AttachScan(parent, session, incomplete: false);
        await WaitForLayoutAsync(vm);

        var fakeAggregate = new TreemapRectangle
        {
            Id = Guid.NewGuid(),
            DisplayName = "Other",
            IsAggregate = true,
            SizeBytes = onlyDir.SizeBytes,
            AggregatedCount = 1,
            AggregatedNodes = new[] { onlyDir },
            Width = 20,
            Height = 20
        };

        vm.ActivateRectangle(fakeAggregate);
        await WaitForLayoutAsync(vm);

        Assert.True(vm.Breadcrumbs.Count >= 2);
        Assert.Contains(vm.Breadcrumbs, b => ReferenceEquals(b.Node, onlyDir));
    }

    [Fact]
    public async Task NestedAggregate_RespectsDepthAndNoOpGuard()
    {
        var (vm, root) = CreateFanoutVm(childCount: 60, tinySize: 5, bigSize: 5_000_000);
        var session = Guid.NewGuid();
        vm.AttachScan(root, session, incomplete: false);
        await WaitForLayoutAsync(vm);

        var other = Assert.Single(vm.Layout!.Rectangles, r => r.IsAggregate);
        vm.ActivateRectangle(other);
        await WaitForLayoutAsync(vm);

        // Re-activate an identical aggregate shape should not stack forever.
        var before = vm.Breadcrumbs.Count;
        var nestedOther = vm.Layout!.Rectangles.FirstOrDefault(r => r.IsAggregate);
        if (nestedOther is not null)
        {
            vm.ActivateRectangle(nestedOther);
            await WaitForLayoutAsync(vm);
            Assert.True(vm.Breadcrumbs.Count <= before + 1);
        }
        else
        {
            // Members fully expanded — drill path completed safely.
            Assert.True(vm.Layout.Rectangles.Count > 0);
        }
    }

    [Fact]
    public async Task Keyboard_MoveFocus_Activate_ClearFocus_KeepFocusOnRectangle()
    {
        var (vm, root, _) = CreateDeepTreeVm();
        var session = Guid.NewGuid();
        vm.AttachScan(root, session, incomplete: false);
        await WaitForLayoutAsync(vm);

        var first = vm.Layout!.Rectangles[0];
        vm.FocusedRectangleId = first.Id;
        vm.MoveFocus(1, 0);
        Assert.NotNull(vm.FocusedRectangleId);

        var focusedBefore = vm.FocusedRectangleId;
        vm.MoveFocus(0, 1);
        // Focus stays on a valid rectangle id from the current layout.
        Assert.Contains(vm.Layout.Rectangles, r => r.Id == vm.FocusedRectangleId);

        vm.SelectRectangle(vm.Layout.Rectangles.First(r => r.Id == focusedBefore || r.Id == vm.FocusedRectangleId));
        Assert.Equal(vm.SelectedRectangleId, vm.FocusedRectangleId);

        vm.ClearFocus();
        Assert.Null(vm.FocusedRectangleId);
        Assert.NotNull(vm.SelectedRectangleId); // selection retained; focus chrome cleared
    }

    [Fact]
    public async Task TooltipCache_ReturnsSameString_UntilLayoutRefresh()
    {
        var (vm, root, _) = CreateDeepTreeVm();
        vm.AttachScan(root, Guid.NewGuid(), incomplete: false);
        await WaitForLayoutAsync(vm);

        var rect = vm.Layout!.Rectangles[0];
        var first = vm.FormatTooltip(rect);
        var second = vm.FormatTooltip(rect);
        Assert.Same(first, second);

        vm.RefreshLocalization();
        await WaitForLayoutAsync(vm);

        var refreshedRect = vm.Layout!.Rectangles.FirstOrDefault(r => r.DisplayName == rect.DisplayName)
                            ?? vm.Layout.Rectangles[0];
        var third = vm.FormatTooltip(refreshedRect);
        Assert.False(ReferenceEquals(first, third));
    }

    [Fact]
    public async Task RapidScanRestart_DiscardsStaleLayout()
    {
        var loc = new StubLocalization();
        var vm = CreateVm(loc);
        var rootA = Dir("A", File("a.bin", 100));
        var rootB = Dir("B", File("b.bin", 200), File("c.bin", 300));

        var sessionA = Guid.NewGuid();
        vm.AttachScan(rootA, sessionA, incomplete: false);
        vm.OnScanStarting();
        Assert.Null(vm.Layout);
        Assert.Equal(Guid.Empty, vm.ScanSessionId);

        var sessionB = Guid.NewGuid();
        vm.AttachScan(rootB, sessionB, incomplete: false);
        await WaitForLayoutAsync(vm);

        Assert.Equal(sessionB, vm.ScanSessionId);
        Assert.Same(rootB, vm.Layout!.ViewRoot);
        Assert.DoesNotContain(vm.Layout.Rectangles, r => r.Source?.Name == "a.bin");
    }

    [Fact]
    public async Task LargeFanout_5000Children_LayoutsWithoutBlockingAndAggregates()
    {
        var (vm, root) = CreateFanoutVm(childCount: 5_000, tinySize: 1, bigSize: 1_000_000);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        vm.AttachScan(root, Guid.NewGuid(), incomplete: false);
        await WaitForLayoutAsync(vm, TimeSpan.FromSeconds(15));
        sw.Stop();

        Assert.NotNull(vm.Layout);
        Assert.True(vm.Layout!.Rectangles.Count <= 400);
        Assert.Contains(vm.Layout.Rectangles, r => r.IsAggregate);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task IncompleteScan_ShowsPartialStatus_AndStillLayouts()
    {
        var (vm, root, _) = CreateDeepTreeVm();
        vm.AttachScan(root, Guid.NewGuid(), incomplete: true);
        await WaitForLayoutAsync(vm);

        Assert.True(vm.FromIncompleteScanVisible);
        Assert.NotNull(vm.Layout);
        Assert.Contains("partial", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    private static (TreemapTabViewModel Vm, FileSystemNode Root, FileSystemNode DeepFile) CreateDeepTreeVm()
    {
        var root = Dir(
            "root",
            Dir("d1", Dir("d2", Dir("d3", File("leaf.bin", 42))), File("sibling.bin", 10)),
            File("rootfile.bin", 5));
        var deepFile = FindByName(root, "leaf.bin")
                       ?? throw new InvalidOperationException("leaf.bin missing");
        var vm = CreateVm(new StubLocalization());
        return (vm, root, deepFile);
    }

    private static FileSystemNode? FindByName(FileSystemNode node, string name)
    {
        if (string.Equals(node.Name, name, StringComparison.Ordinal))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found = FindByName(child, name);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static (TreemapTabViewModel Vm, FileSystemNode Root) CreateFanoutVm(int childCount, long tinySize, long bigSize)
    {
        var children = new List<FileSystemNode> { File("big.bin", bigSize) };
        for (var i = 0; i < childCount; i++)
        {
            children.Add(File($"tiny{i}.bin", tinySize));
        }

        var root = Dir("root", children.ToArray());
        return (CreateVm(new StubLocalization()), root);
    }

    private static TreemapTabViewModel CreateVm(ILocalizationService localization) =>
        new(new NoopFileActions(), localization, NullLogger<TreemapTabViewModel>.Instance);

    private static async Task WaitForLayoutAsync(TreemapTabViewModel vm, TimeSpan? timeout = null)
    {
        await WaitForAsync(() => vm.Layout is not null && !vm.IsLayoutBusy, timeout ?? TimeSpan.FromSeconds(5));
    }

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan? timeout = null)
    {
        var limit = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < limit)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.True(predicate(), "Timed out waiting for condition.");
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
            // Rebuild child with correct path under this parent (FullPath is init-only).
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
            FullPath = System.IO.Path.Combine(parentPath, source.Name),
            IsDirectory = false,
            SizeBytes = source.SizeBytes,
            FileCount = 1,
            LastModified = source.LastModified
        };

    private static FileSystemNode RelocateDirectory(FileSystemNode source, string parentPath)
    {
        var relocated = new FileSystemNode
        {
            Name = source.Name,
            FullPath = System.IO.Path.Combine(parentPath, source.Name),
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

    private static FileSystemNode File(string name, long size) =>
        new()
        {
            Name = name,
            FullPath = @"C:\tests\" + name,
            IsDirectory = false,
            SizeBytes = size,
            FileCount = 1,
            LastModified = DateTimeOffset.UtcNow
        };

    private sealed class NoopFileActions : IFileActionService
    {
        public bool TryOpenContainingFolder(string fullPath, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }

        public bool TryCopyPath(string fullPath, out string? errorMessage)
        {
            errorMessage = null;
            return true;
        }
    }

    private sealed class StubLocalization : ILocalizationService
    {
        private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal)
        {
            ["Treemap_Other"] = "Other",
            ["Treemap_Ready"] = "Ready",
            ["Treemap_PartialResults"] = "Partial results",
            ["Treemap_NoScanResults"] = "No scan results",
            ["Treemap_LayoutFailed"] = "Layout failed",
            ["Treemap_OtherAggregateFormat"] = "{0} items",
            ["Treemap_Type_OtherAggregate"] = "Other aggregate",
            ["Treemap_Type_Folder"] = "Folder",
            ["Treemap_LogicalSize"] = "Size",
            ["Treemap_Percentage"] = "Percentage",
            ["Treemap_Category"] = "Category",
            ["Treemap_Modified"] = "Modified",
            ["Treemap_AggregateDepthLimit"] = "Depth limit",
            ["Treemap_AggregateCannotExpand"] = "Cannot expand",
            ["Treemap_Legend_Folder"] = "Folders",
            ["FileTypes_Category_Videos"] = "Videos",
            ["FileTypes_Category_Images"] = "Images",
            ["FileTypes_Category_Audio"] = "Audio",
            ["FileTypes_Category_Documents"] = "Documents",
            ["FileTypes_Category_Archives"] = "Archives",
            ["FileTypes_Category_Applications"] = "Applications",
            ["FileTypes_Category_Other"] = "Other",
            ["EmDash"] = "—"
        };

        public AppLanguageMode Preference => AppLanguageMode.English;

        public AppLanguageMode EffectiveLanguage => AppLanguageMode.English;

        public CultureInfo CurrentCulture { get; } = CultureInfo.GetCultureInfo("en-US");

        public IReadOnlyList<LanguageOption> AvailableLanguages { get; } = [];

        public event EventHandler? LanguageChanged;

        public void Initialize()
        {
        }

        public void SetPreference(AppLanguageMode mode)
        {
            _ = mode;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SwitchToTurkish()
        {
        }

        public string GetString(string key) =>
            _map.TryGetValue(key, out var value) ? value : key;

        public string GetString(string key, params object[] args) =>
            string.Format(CurrentCulture, GetString(key), args);

        public bool HasKey(string key) => _map.ContainsKey(key);

        public IReadOnlyCollection<string> GetAllKeys() => _map.Keys.ToList();

        public void Dispose()
        {
        }
    }
}
