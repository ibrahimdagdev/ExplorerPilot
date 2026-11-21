using ExplorerPilot.Core.Enums;
using ExplorerPilot.Core.Models;
using ExplorerPilot.Infrastructure.Services;

namespace ExplorerPilot.Tests;

public sealed class FileSearchServiceTests
{
    private readonly FileSearchService _search = new();

    [Fact]
    public async Task Search_MatchesNameSubstring_CaseInsensitive()
    {
        var root = BuildTree();

        var page = await _search.SearchAsync(root, Criteria(query: "RAPOR"), fromIncompleteScan: false);

        Assert.Contains(page.Items, h => h.Name.Equals("rapor-öğrenci.txt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Search_MatchesFullPath()
    {
        var root = BuildTree();

        var page = await _search.SearchAsync(
            root,
            Criteria(query: "docs", scope: SearchScope.FullPath),
            fromIncompleteScan: false);

        Assert.Contains(page.Items, h => h.FullPath.Contains("docs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_MatchesFolderNameScope()
    {
        var root = BuildTree();

        var page = await _search.SearchAsync(
            root,
            Criteria(query: "docs", scope: SearchScope.FolderName, kind: SearchItemKind.FilesOnly),
            fromIncompleteScan: false);

        Assert.Contains(page.Items, h => h.Name == "rapor-öğrenci.txt");
        Assert.DoesNotContain(page.Items, h => h.IsDirectory);
    }

    [Fact]
    public async Task Search_FiltersByExtension()
    {
        var root = BuildTree();

        var page = await _search.SearchAsync(
            root,
            Criteria(extension: ".MP4", kind: SearchItemKind.FilesOnly),
            fromIncompleteScan: false);

        Assert.All(page.Items, h => Assert.False(h.IsDirectory));
        Assert.Contains(page.Items, h => h.Name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                                         || h.Name.EndsWith(".MP4", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(page.Items, h => h.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_FiltersBySizeBounds()
    {
        var root = BuildTree();

        var page = await _search.SearchAsync(
            root,
            Criteria(minSize: 100, maxSize: 200, kind: SearchItemKind.FilesOnly),
            fromIncompleteScan: false);

        Assert.All(page.Items, h =>
        {
            Assert.InRange(h.SizeBytes, 100, 200);
        });
        Assert.Contains(page.Items, h => h.Name == "mid.bin");
    }

    [Fact]
    public async Task Search_FiltersByModifiedDate()
    {
        var root = BuildTree();
        var from = DateTimeOffset.UtcNow.AddDays(-2);
        var to = DateTimeOffset.UtcNow.AddDays(1);

        var page = await _search.SearchAsync(
            root,
            Criteria(modifiedFrom: from, modifiedTo: to, kind: SearchItemKind.FilesOnly),
            fromIncompleteScan: false);

        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, h =>
        {
            Assert.NotNull(h.LastModifiedUtc);
            Assert.True(h.LastModifiedUtc >= from);
            Assert.True(h.LastModifiedUtc <= to);
        });
    }

    [Fact]
    public async Task Search_FilesOnly_And_FoldersOnly()
    {
        var root = BuildTree();

        var files = await _search.SearchAsync(root, Criteria(kind: SearchItemKind.FilesOnly), false);
        var folders = await _search.SearchAsync(root, Criteria(kind: SearchItemKind.FoldersOnly), false);

        Assert.All(files.Items, h => Assert.False(h.IsDirectory));
        Assert.All(folders.Items, h => Assert.True(h.IsDirectory));
        Assert.Contains(folders.Items, h => h.Name == "docs");
    }

    [Fact]
    public async Task Search_TurkishCharacters()
    {
        var root = BuildTree();

        var page = await _search.SearchAsync(root, Criteria(query: "öğrenci"), false);

        Assert.Contains(page.Items, h => h.Name.Contains("öğrenci", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Search_EmptyQuery_ReturnsItemsHonoringOtherFilters()
    {
        var root = BuildTree();

        var page = await _search.SearchAsync(root, Criteria(kind: SearchItemKind.FilesOnly), false);

        Assert.True(page.CollectedMatchCount >= 3);
    }

    [Fact]
    public async Task Search_Cancellation_Throws()
    {
        var root = BuildLargeTree(5_000);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await _search.SearchAsync(root, Criteria(), false, cts.Token));
    }

    [Fact]
    public async Task Search_Pagination_ReturnsStablePages()
    {
        var root = BuildLargeTree(250);
        var session = Guid.NewGuid();

        var page0 = await _search.SearchAsync(
            root,
            Criteria(session: session, kind: SearchItemKind.FilesOnly, pageIndex: 0, pageSize: 100),
            false);
        var page1 = await _search.SearchAsync(
            root,
            Criteria(session: session, kind: SearchItemKind.FilesOnly, pageIndex: 1, pageSize: 100),
            false);

        Assert.Equal(100, page0.Items.Count);
        Assert.True(page1.Items.Count > 0);
        Assert.Equal(0, page0.PageIndex);
        Assert.Equal(1, page1.PageIndex);
        Assert.True(page0.HasNextPage);
        Assert.True(page1.HasPreviousPage);
        Assert.Empty(page0.Items.Select(i => i.FullPath).Intersect(page1.Items.Select(i => i.FullPath)));
    }

    [Fact]
    public async Task Search_SortBySize_IsStableWithPathTieBreak()
    {
        var root = new FileSystemNode
        {
            Name = "root",
            FullPath = @"C:\root",
            IsDirectory = true,
            Children =
            {
                File(@"C:\root\b.bin", "b.bin", 50),
                File(@"C:\root\a.bin", "a.bin", 50),
                File(@"C:\root\c.bin", "c.bin", 100)
            }
        };

        var page = await _search.SearchAsync(
            root,
            Criteria(kind: SearchItemKind.FilesOnly, sort: SearchSortField.Size, direction: SearchSortDirection.Descending),
            false);

        Assert.Equal(new[] { "c.bin", "a.bin", "b.bin" }, page.Items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task Search_IncompleteFlag_IsPreserved()
    {
        var root = BuildTree();
        var page = await _search.SearchAsync(root, Criteria(), fromIncompleteScan: true);
        Assert.True(page.FromIncompleteScan);
    }

    [Fact]
    public async Task Search_DoesNotEnumerateFilesystem()
    {
        // Root path does not exist on disk; search still works on in-memory nodes.
        var root = new FileSystemNode
        {
            Name = "ghost",
            FullPath = @"C:\ExplorerPilot-Does-Not-Exist-" + Guid.NewGuid(),
            IsDirectory = true,
            Children =
            {
                File(@"C:\ExplorerPilot-Does-Not-Exist\a.txt", "a.txt", 10)
            }
        };

        Assert.False(Directory.Exists(root.FullPath));
        var page = await _search.SearchAsync(root, Criteria(query: "a.txt"), false);
        Assert.Contains(page.Items, h => h.Name == "a.txt");
    }

    [Fact]
    public async Task Search_RespectsMaxCollectibleMatches()
    {
        var root = BuildLargeTree(300);
        var page = await _search.SearchAsync(
            root,
            Criteria(kind: SearchItemKind.FilesOnly, maxCollect: 50, pageSize: 10),
            false);

        Assert.True(page.ResultsTruncated);
        Assert.Equal(50, page.CollectedMatchCount);
        Assert.Equal(10, page.Items.Count);
    }

    [Fact]
    public async Task Search_SessionId_IsEchoed()
    {
        var session = Guid.NewGuid();
        var page = await _search.SearchAsync(BuildTree(), Criteria(session: session), false);
        Assert.Equal(session, page.ScanSessionId);
    }

    [Fact]
    public void FileAction_Resolve_MissingFileUsesParent()
    {
        using var temp = TemporaryDirectory.Create();
        var missing = Path.Combine(temp.Path, "gone.txt");
        var plan = ExplorerPilot.App.Services.FileActionService.ResolveOpenContainingFolder(missing);
        Assert.Equal(ExplorerPilot.App.Services.FileActionService.OpenFolderKind.OpenDirectory, plan.Kind);
    }

    private static FileSearchCriteria Criteria(
        string? query = null,
        SearchScope scope = SearchScope.NameOrPath,
        string? extension = null,
        long? minSize = null,
        long? maxSize = null,
        DateTimeOffset? modifiedFrom = null,
        DateTimeOffset? modifiedTo = null,
        SearchItemKind kind = SearchItemKind.All,
        SearchSortField sort = SearchSortField.Size,
        SearchSortDirection direction = SearchSortDirection.Descending,
        int pageIndex = 0,
        int pageSize = 100,
        int maxCollect = FileSearchCriteria.DefaultMaxCollectibleMatches,
        Guid? session = null) =>
        new()
        {
            ScanSessionId = session ?? Guid.NewGuid(),
            Query = query,
            Scope = scope,
            ExtensionFilter = extension,
            MinSizeBytes = minSize,
            MaxSizeBytes = maxSize,
            ModifiedFromUtc = modifiedFrom,
            ModifiedToUtc = modifiedTo,
            ItemKind = kind,
            SortBy = sort,
            SortDirection = direction,
            PageIndex = pageIndex,
            PageSize = pageSize,
            MaxCollectibleMatches = maxCollect
        };

    private static FileSystemNode BuildTree()
    {
        var now = DateTimeOffset.UtcNow;
        var docs = new FileSystemNode
        {
            Name = "docs",
            FullPath = @"C:\scan\docs",
            IsDirectory = true,
            LastModified = now,
            Children =
            {
                File(@"C:\scan\docs\rapor-öğrenci.txt", "rapor-öğrenci.txt", 40, now)
            }
        };

        var root = new FileSystemNode
        {
            Name = "scan",
            FullPath = @"C:\scan",
            IsDirectory = true,
            LastModified = now,
            Children =
            {
                docs,
                File(@"C:\scan\small.bin", "small.bin", 10, now),
                File(@"C:\scan\mid.bin", "mid.bin", 150, now),
                File(@"C:\scan\video.MP4", "video.MP4", 500, now)
            }
        };

        docs.Parent = root;
        foreach (var child in root.Children)
        {
            child.Parent = root;
        }

        foreach (var child in docs.Children)
        {
            child.Parent = docs;
        }

        return root;
    }

    private static FileSystemNode BuildLargeTree(int fileCount)
    {
        var root = new FileSystemNode
        {
            Name = "root",
            FullPath = @"C:\large",
            IsDirectory = true
        };

        for (var i = 0; i < fileCount; i++)
        {
            var file = File($@"C:\large\f{i:D5}.bin", $"f{i:D5}.bin", i + 1);
            file.Parent = root;
            root.Children.Add(file);
        }

        return root;
    }

    private static FileSystemNode File(string path, string name, long size, DateTimeOffset? modified = null) =>
        new()
        {
            Name = name,
            FullPath = path,
            IsDirectory = false,
            SizeBytes = size,
            FileCount = 1,
            LastModified = modified ?? DateTimeOffset.UtcNow,
            ScanStatus = NodeScanStatus.Completed
        };
}
