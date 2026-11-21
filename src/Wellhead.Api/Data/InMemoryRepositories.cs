using Wellhead.Api.Demo;
using Wellhead.Api.Domain;

namespace Wellhead.Api.Data;

/// <summary>In-memory master data, used only when no Oracle connection string is configured.</summary>
public sealed class InMemoryWellRepository : IWellRepository
{
    private readonly object _gate = new();
    private readonly List<Well> _wells = DemoData.Wells().ToList();

    public Task<IReadOnlyList<Well>> ListWellsAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Well>>(_wells.OrderBy(w => w.WellId, StringComparer.Ordinal).ToList());
        }
    }

    public Task<Well?> GetWellAsync(string wellId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_wells.FirstOrDefault(w => w.WellId == wellId));
        }
    }

    public Task<int> UpsertWellsAsync(IReadOnlyList<Well> wells, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var well in wells)
            {
                var index = _wells.FindIndex(w => w.WellId == well.WellId);
                if (index >= 0)
                {
                    _wells[index] = well;
                }
                else
                {
                    _wells.Add(well);
                }
            }

            return Task.FromResult(wells.Count);
        }
    }
}

/// <summary>In-memory telemetry, used only when no SingleStore connection string is configured.</summary>
public sealed class InMemoryTelemetryRepository : ITelemetryRepository
{
    private readonly object _gate = new();
    private readonly List<ProductionWindow> _windows;

    public InMemoryTelemetryRepository()
    {
        _windows = DemoData.Windows().ToList();
    }

    public Task<IReadOnlyList<ProductionWindow>> LatestWindowsAsync(
        string wellId,
        int limit,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var rows = _windows
                .Where(w => w.WellId == wellId)
                .OrderByDescending(w => w.WindowStart)
                .Take(Math.Clamp(limit, 1, 5_000))
                .ToList();
            return Task.FromResult<IReadOnlyList<ProductionWindow>>(rows);
        }
    }

    public Task<IReadOnlyList<FlaggedWindow>> FlaggedWindowsAsync(
        DateTimeOffset since,
        int limit,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            // Demo alerting: a window producing under 55% of its well's median
            // rate is flagged. In the deployed architecture the Python scoring
            // service writes these rows and this repository only reads them.
            var grouped = _windows.GroupBy(w => w.WellId);
            var alerts = new List<FlaggedWindow>();
            foreach (var group in grouped)
            {
                var ordered = group.OrderBy(w => w.AvgFlowBbl).ToList();
                var median = ordered[ordered.Count / 2].AvgFlowBbl;
                alerts.AddRange(group
                    .Where(w => w.WindowStart >= since && median > 0 && w.AvgFlowBbl < median * 0.55)
                    .Select(w => new FlaggedWindow
                    {
                        WellId = w.WellId,
                        WindowStart = w.WindowStart,
                        Score = Math.Round(0.55 + 0.4 * (1 - w.AvgFlowBbl / median), 4),
                        Decision = "flag",
                        Reasons = "[\"flow_drop_ratio below 0.45 of trailing baseline\"]"
                    }));
            }

            return Task.FromResult<IReadOnlyList<FlaggedWindow>>(
                alerts.OrderByDescending(a => a.WindowStart).Take(Math.Clamp(limit, 1, 1_000)).ToList());
        }
    }

    public Task<int> UpsertWindowsAsync(
        IReadOnlyList<ProductionWindow> windows,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            foreach (var window in windows)
            {
                var index = _windows.FindIndex(w =>
                    w.WellId == window.WellId && w.WindowStart == window.WindowStart);
                if (index >= 0)
                {
                    _windows[index] = window;
                }
                else
                {
                    _windows.Add(window);
                }
            }

            return Task.FromResult(windows.Count);
        }
    }

    public Task<int> CountWindowsAsync(string wellId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_windows.Count(w => w.WellId == wellId));
        }
    }
}
