using ExplorerPilot.Core.Scheduling;
using ExplorerPilot.Infrastructure.Scheduling;

namespace ExplorerPilot.Tests;

public sealed class ScheduledScanStoreTests
{
    [Fact]
    public async Task SaveLoad_RoundTripsPlans()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "scheduled-scans.json");
        var store = new ScheduledScanStore(path);

        var plan = new ScheduledScanDefinition
        {
            Name = "Nightly",
            TargetPath = @"D:\Data",
            Frequency = ScheduledScanFrequency.Daily,
            LocalTimeOfDay = new TimeSpan(2, 15, 0),
            IsEnabled = true,
            MonthlyDay = 15,
            WeeklyDay = DayOfWeek.Friday,
            NextRunUtc = DateTimeOffset.Parse("2026-11-23T00:15:00Z"),
            LastOutcome = ScheduledScanRunOutcome.Succeeded
        };

        await store.SaveAsync([plan]);
        var loaded = await store.LoadAsync();

        var round = Assert.Single(loaded);
        Assert.Equal(plan.Id, round.Id);
        Assert.Equal("Nightly", round.Name);
        Assert.Equal(@"D:\Data", round.TargetPath);
        Assert.Equal(ScheduledScanFrequency.Daily, round.Frequency);
        Assert.Equal(new TimeSpan(2, 15, 0), round.LocalTimeOfDay);
        Assert.Equal(DayOfWeek.Friday, round.WeeklyDay);
        Assert.Equal(15, round.MonthlyDay);
        Assert.Equal(ScheduledScanRunOutcome.Succeeded, round.LastOutcome);
        Assert.Equal(plan.NextRunUtc, round.NextRunUtc);
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsEmpty()
    {
        using var temp = TemporaryDirectory.Create();
        var store = new ScheduledScanStore(Path.Combine(temp.Path, "missing.json"));
        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task Load_CorruptJson_ReturnsEmpty()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "scheduled-scans.json");
        await File.WriteAllTextAsync(path, "{ not-valid-json");
        var store = new ScheduledScanStore(path);
        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task Save_UsesAtomicTempThenReplace()
    {
        using var temp = TemporaryDirectory.Create();
        var path = Path.Combine(temp.Path, "scheduled-scans.json");
        var store = new ScheduledScanStore(path);
        await store.SaveAsync([
            new ScheduledScanDefinition { Name = "a", TargetPath = @"C:\", Frequency = ScheduledScanFrequency.Manual }
        ]);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
        var text = await File.ReadAllTextAsync(path);
        Assert.Contains("\"name\": \"a\"", text, StringComparison.Ordinal);
    }
}
