using Wellhead.Api.Data;
using Wellhead.Api.Domain;

namespace Wellhead.Api.Tests;

/// <summary>Hand-written fakes keep service tests free of database and broker setup.</summary>
internal sealed class FakeWellRepository : IWellRepository
{
    private readonly List<Well> _wells;

    public FakeWellRepository(IEnumerable<Well>? wells = null)
    {
        _wells = wells?.ToList() ?? new List<Well>
        {
            new() { WellId = "W-0001", Name = "Rattlesnake 12H", Basin = "Delaware", County = "Loving", Status = "producing" }
        };
    }

    public Task<IReadOnlyList<Well>> ListWellsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Well>>(_wells);

    public Task<Well?> GetWellAsync(string wellId, CancellationToken cancellationToken) =>
        Task.FromResult(_wells.FirstOrDefault(w => w.WellId == wellId));

    public Task<int> UpsertWellsAsync(IReadOnlyList<Well> wells, CancellationToken cancellationToken)
    {
        _wells.AddRange(wells);
        return Task.FromResult(wells.Count);
    }
}

internal sealed class FakeTelemetryRepository : ITelemetryRepository
{
    private readonly List<ProductionWindow> _windows;

    public FakeTelemetryRepository(IEnumerable<ProductionWindow> windows) => _windows = windows.ToList();

    public Task<IReadOnlyList<ProductionWindow>> LatestWindowsAsync(
        string wellId,
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProductionWindow>>(_windows
            .Where(w => w.WellId == wellId)
            .OrderByDescending(w => w.WindowStart)
            .Take(limit)
            .ToList());

    public Task<IReadOnlyList<FlaggedWindow>> FlaggedWindowsAsync(
        DateTimeOffset since,
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FlaggedWindow>>(Array.Empty<FlaggedWindow>());

    public Task<int> UpsertWindowsAsync(
        IReadOnlyList<ProductionWindow> windows,
        CancellationToken cancellationToken)
    {
        _windows.AddRange(windows);
        return Task.FromResult(windows.Count);
    }

    public Task<int> CountWindowsAsync(string wellId, CancellationToken cancellationToken) =>
        Task.FromResult(_windows.Count(w => w.WellId == wellId));
}

internal static class TestWindows
{
    /// <summary>Arps-consistent five-minute windows for a single well.</summary>
    public static List<ProductionWindow> Arps(
        int count = 24,
        double qi = 1000d,
        double diPerDay = 0.01d,
        string wellId = "W-0001",
        DateTimeOffset? start = null)
    {
        var anchor = start ?? new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var intervalDays = 5d / 1440d;
        var windows = new List<ProductionWindow>(count);
        for (var index = 0; index < count; index++)
        {
            var windowStart = anchor.AddMinutes(5 * index);
            var rate = qi * Math.Exp(-diPerDay * index * intervalDays);
            windows.Add(new ProductionWindow
            {
                WellId = wellId,
                WindowStart = windowStart,
                WindowEnd = windowStart.AddMinutes(5),
                SampleCount = 5,
                ExpectedSamples = 5,
                AvgFlowBbl = rate,
                TotalVolumeBbl = rate * 5d / 1440d,
                FlowCv = 0.01,
                AvgPressurePsi = 1500,
                MaxPressurePsi = 1520,
                AvgTempF = 150
            });
        }

        return windows;
    }
}
