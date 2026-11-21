using ExplorerPilot.Core.Models;

namespace ExplorerPilot.Core.Interfaces;

/// <summary>
/// Builds dashboard aggregates off the UI thread from drives + optional scan/snapshot/cleanup inputs.
/// </summary>
public interface IDashboardSummaryService
{
    Task<DashboardSummary> BuildAsync(
        DashboardBuildRequest request,
        CancellationToken cancellationToken = default);
}
