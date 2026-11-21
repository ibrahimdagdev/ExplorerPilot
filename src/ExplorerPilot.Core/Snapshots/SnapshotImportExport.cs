namespace ExplorerPilot.Core.Snapshots;

/// <summary>How to resolve an imported snapshot whose id already exists in the store.</summary>
public enum SnapshotIdCollisionPolicy
{
    /// <summary>Leave the existing snapshot untouched and abort import.</summary>
    KeepExisting = 0,

    /// <summary>Overwrite the sandboxed snapshot file with the imported content (same id).</summary>
    ReplaceExisting = 1,

    /// <summary>Assign a new id and keep both snapshots.</summary>
    AssignNewId = 2
}

public sealed class SnapshotImportResult
{
    public required SnapshotInfo Info { get; init; }

    public bool IdWasReassigned { get; init; }

    public bool ReplacedExisting { get; init; }

    public Guid? OriginalId { get; init; }
}

public sealed class SnapshotValidationException : Exception
{
    public SnapshotValidationException(string message)
        : base(message)
    {
    }

    public SnapshotValidationException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
