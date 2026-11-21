namespace ExplorerPilot.Core.Reporting;

/// <summary>How complete the underlying scan was when the report was built.</summary>
public enum ReportCompleteness
{
    Complete = 0,
    Cancelled = 1,
    Failed = 2,
    Warnings = 3
}
