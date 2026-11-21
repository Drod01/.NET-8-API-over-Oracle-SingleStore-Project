using Wellhead.Api.Data;
using Wellhead.Api.Domain;

namespace Wellhead.Api.Services;

/// <summary>
/// Aggregates warehouse windows into the KPI shape internal consumers ask for.
/// </summary>
public sealed class KpiService(ITelemetryRepository telemetry, IWellRepository wells)
{
    /// <summary>24 hours of five-minute windows.</summary>
    public const int DefaultWindowLookback = 288;

    public async Task<IReadOnlyList<WellKpi>> GetAsync(int limit, CancellationToken cancellationToken)
    {
        var wellList = await wells.ListWellsAsync(cancellationToken);
        var result = new List<WellKpi>(wellList.Count);
        foreach (var well in wellList.Take(Math.Clamp(limit, 1, 500)))
        {
            var windows = await telemetry.LatestWindowsAsync(well.WellId, DefaultWindowLookback, cancellationToken);
            if (windows.Count == 0)
            {
                continue;
            }

            var ordered = windows.OrderBy(w => w.WindowStart).ToList();
            var volume = ordered.Sum(w => w.TotalVolumeBbl);
            var hours = ordered.Sum(w => w.WindowHours);
            var avgFlow = hours > 0 ? volume / (hours / 24d) : 0d;
            var intervalDays = ordered.Count > 1
                ? (ordered[^1].WindowStart - ordered[0].WindowStart).TotalDays / (ordered.Count - 1)
                : 0d;

            result.Add(new WellKpi(
                well.WellId,
                well.Name,
                well.Basin,
                Math.Round(volume, 3),
                Math.Round(avgFlow, 2),
                Math.Round(ordered.Average(w => w.CoverageRatio), 4),
                Math.Round(DeclineCurve.MonthlyDeclinePct(ordered.Select(w => w.AvgFlowBbl).ToList(), intervalDays), 2),
                ordered.Count));
        }

        return result;
    }
}

/// <summary>Fits Arps decline to recent production and projects a P10/P50/P90 band.</summary>
public sealed class ForecastService(ITelemetryRepository telemetry, IWellRepository wells)
{
    public async Task<WellForecast?> GetAsync(string wellId, int horizonDays, CancellationToken cancellationToken)
    {
        var well = await wells.GetWellAsync(wellId, cancellationToken);
        if (well is null)
        {
            return null;
        }

        var windows = await telemetry.LatestWindowsAsync(wellId, KpiService.DefaultWindowLookback, cancellationToken);
        var ordered = windows
            .Where(w => w.AvgFlowBbl > 0)
            .OrderBy(w => w.WindowStart)
            .ToList();
        if (ordered.Count < 4)
        {
            return new WellForecast(
                well.WellId, well.Name, 0, 0, 0, 0, ordered.Count,
                Array.Empty<ForecastPoint>(), DeclineCurve.ModelVersion);
        }

        var intervalDays = (ordered[^1].WindowStart - ordered[0].WindowStart).TotalDays / (ordered.Count - 1);
        var rates = ordered.Select(w => w.AvgFlowBbl).ToList();
        var fit = DeclineCurve.Fit(rates, intervalDays);
        if (fit is null)
        {
            return new WellForecast(
                well.WellId, well.Name, 0, 0, 0, 0, ordered.Count,
                Array.Empty<ForecastPoint>(), DeclineCurve.ModelVersion);
        }

        var horizon = Math.Clamp(horizonDays, 1, 90);
        var points = DeclineCurve.Forecast(
            rates, intervalDays, horizon, DateOnly.FromDateTime(ordered[^1].WindowStart.UtcDateTime));

        return new WellForecast(
            well.WellId,
            well.Name,
            Math.Round(fit.QiBpd, 2),
            Math.Round(fit.DiPerDay, 6),
            Math.Round(fit.RSquared, 4),
            Math.Round(fit.MonthlyDeclinePct, 2),
            ordered.Count,
            points,
            DeclineCurve.ModelVersion);
    }
}
