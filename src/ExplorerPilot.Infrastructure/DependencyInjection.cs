using ExplorerPilot.Core.Interfaces;
using ExplorerPilot.Infrastructure.Reporting;
using ExplorerPilot.Infrastructure.Scanning;
using ExplorerPilot.Infrastructure.Services;
using ExplorerPilot.Infrastructure.Snapshots;
using Microsoft.Extensions.DependencyInjection;

namespace ExplorerPilot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddExplorerPilotInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDiskScanner, DiskScanner>();
        services.AddSingleton<IDriveDiscoveryService, DriveDiscoveryService>();
        services.AddSingleton<IDriveInfoProvider, DriveInfoProvider>();
        services.AddSingleton<IDashboardSummaryService, DashboardSummaryService>();
        services.AddSingleton<IScheduledScanStore, Scheduling.ScheduledScanStore>();
        services.AddSingleton<IScheduledScanService, Scheduling.ScheduledScanService>();
        services.AddSingleton<ISystemInformationProvider, WindowsSystemInformationProvider>();
        services.AddSingleton<IFileSearchService, FileSearchService>();
        services.AddSingleton<IDuplicateFileFinderService, DuplicateFileFinderService>();
        services.AddSingleton<IReportExportService, ReportExportService>();
        services.AddSingleton<ISnapshotCatalogNotifier, SnapshotCatalogNotifier>();
        services.AddSingleton<ISnapshotStore, SnapshotStore>();
        services.AddSingleton<ISnapshotComparisonService, SnapshotComparisonService>();
        services.AddSingleton<ISnapshotTrendService, SnapshotTrendService>();
        services.AddSingleton<ICleanupRecommendationService, CleanupRecommendationService>();
        services.AddSingleton<ICleanupExportService, CleanupExportService>();
        return services;
    }
}
