namespace ExplorerPilot.Core.Enums;

public enum SearchItemKind
{
    All,
    FilesOnly,
    FoldersOnly
}

public enum SearchScope
{
    /// <summary>Match against the item name only.</summary>
    Name,

    /// <summary>Match against folder names (directories and parent folders of files).</summary>
    FolderName,

    /// <summary>Match against the full path.</summary>
    FullPath,

    /// <summary>Match name or full path.</summary>
    NameOrPath
}

public enum SearchSortField
{
    Name,
    Size,
    Modified
}

public enum SearchSortDirection
{
    Ascending,
    Descending
}

public enum FileSizeUnit
{
    KB,
    MB,
    GB,
    TB
}
