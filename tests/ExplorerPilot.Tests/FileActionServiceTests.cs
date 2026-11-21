using ExplorerPilot.App.Services;

namespace ExplorerPilot.Tests;

public sealed class FileActionServiceTests
{
    [Fact]
    public void ResolveOpenContainingFolder_EmptyPath_IsInvalid()
    {
        var plan = FileActionService.ResolveOpenContainingFolder("  ");
        Assert.Equal(FileActionService.OpenFolderKind.Invalid, plan.Kind);
    }

    [Fact]
    public void ResolveOpenContainingFolder_MissingFileAndParent_IsMissing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "ExplorerPilot-Missing-" + Guid.NewGuid(), "gone.txt");
        var plan = FileActionService.ResolveOpenContainingFolder(missing);
        Assert.Equal(FileActionService.OpenFolderKind.Missing, plan.Kind);
    }

    [Fact]
    public void ResolveOpenContainingFolder_MissingFile_OpensExistingParent()
    {
        using var temp = TemporaryDirectory.Create();
        var missingFile = Path.Combine(temp.Path, "deleted-after-scan.bin");

        var plan = FileActionService.ResolveOpenContainingFolder(missingFile);

        Assert.Equal(FileActionService.OpenFolderKind.OpenDirectory, plan.Kind);
        Assert.Equal(temp.Path, plan.TargetPath);
    }

    [Fact]
    public void ResolveOpenContainingFolder_ExistingFile_SelectsFile()
    {
        using var temp = TemporaryDirectory.Create();
        var file = Path.Combine(temp.Path, "alive.bin");
        File.WriteAllBytes(file, [1, 2, 3]);

        var plan = FileActionService.ResolveOpenContainingFolder(file);

        Assert.Equal(FileActionService.OpenFolderKind.SelectFile, plan.Kind);
        Assert.Equal(file, plan.TargetPath);
    }

    [Fact]
    public void TryCopyPath_EmptyPath_ReturnsFalse()
    {
        var actions = new FileActionService();
        var ok = actions.TryCopyPath("", out var error);
        Assert.False(ok);
        Assert.Equal("empty", error);
    }

    [Fact]
    public void TryOpenContainingFolder_MissingPath_ReturnsFalse()
    {
        var actions = new FileActionService();
        var missing = Path.Combine(Path.GetTempPath(), "ExplorerPilot-Missing-" + Guid.NewGuid(), "gone.txt");
        var ok = actions.TryOpenContainingFolder(missing, out var error);
        Assert.False(ok);
        Assert.Equal("missing", error);
    }
}
