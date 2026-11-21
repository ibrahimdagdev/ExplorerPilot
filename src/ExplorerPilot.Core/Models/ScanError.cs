using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Models;

/// <summary>
/// Lightweight record of a path that could not be fully scanned.
/// Full exception objects are intentionally not retained.
/// </summary>
public sealed class ScanError
{
    public required string Path { get; init; }

    public ScanErrorType ErrorType { get; init; }

    public required string Message { get; init; }
}
