using ExplorerPilot.Core.Models;
using ExplorerPilot.Core.Treemap;

namespace ExplorerPilot.Tests;

/// <summary>
/// Scan-session invalidation semantics used by TreemapTabViewModel (layout versioning).
/// </summary>
public sealed class TreemapSessionTests
{
    [Fact]
    public void StaleLayout_IsDiscardedWhenSessionChanges()
    {
        var engine = new TreemapLayoutEngine();
        var rootA = new FileSystemNode
        {
            Name = "A",
            FullPath = @"C:\A",
            IsDirectory = true,
            SizeBytes = 100,
            Children =
            {
                new FileSystemNode
                {
                    Name = "a.bin",
                    FullPath = @"C:\A\a.bin",
                    IsDirectory = false,
                    SizeBytes = 100
                }
            }
        };
        var rootB = new FileSystemNode
        {
            Name = "B",
            FullPath = @"C:\B",
            IsDirectory = true,
            SizeBytes = 200,
            Children =
            {
                new FileSystemNode
                {
                    Name = "b.bin",
                    FullPath = @"C:\B\b.bin",
                    IsDirectory = false,
                    SizeBytes = 200
                }
            }
        };

        var session = Guid.NewGuid();
        var layoutVersion = 0;
        TreemapLayoutResult? applied = null;

        void Apply(TreemapLayoutResult layout, Guid layoutSession, int version)
        {
            if (version != layoutVersion || layoutSession != session)
            {
                return;
            }

            applied = layout;
        }

        var v1 = ++layoutVersion;
        var layout1 = engine.Layout(rootA, 200, 200);
        session = Guid.NewGuid(); // new scan
        var v2 = ++layoutVersion;
        Apply(layout1, Guid.Empty, v1); // stale
        Assert.Null(applied);

        var layout2 = engine.Layout(rootB, 200, 200);
        Apply(layout2, session, v2);
        Assert.NotNull(applied);
        Assert.Equal(rootB, applied!.ViewRoot);
    }

    [Fact]
    public async Task CancellationToken_StopsBackgroundLayoutWork()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ran = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await Task.Run(() =>
            {
                cts.Token.ThrowIfCancellationRequested();
                ran = true;
            }, cts.Token);
        });

        Assert.False(ran);
    }
}
