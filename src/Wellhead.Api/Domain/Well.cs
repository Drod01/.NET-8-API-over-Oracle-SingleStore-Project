namespace Wellhead.Api.Domain;

/// <summary>Master data, owned by Oracle in this architecture.</summary>
public sealed class Well
{
    public string WellId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Basin { get; init; } = string.Empty;
    public string? County { get; init; }
    public string Status { get; init; } = "producing";
    public DateOnly? SpudDate { get; init; }
}

/// <summary>Flattened KPI row for dashboards and internal consumers.</summary>
public sealed record WellKpi(
    string WellId,
    string Name,
    string Basin,
    double VolumeBbl,
    double AvgFlowBpd,
    double CoverageRatio,
    double MonthlyDeclinePct,
    int Windows);

/// <summary>An alert raised by the scoring service.</summary>
public sealed class FlaggedWindow
{
    public string WellId { get; init; } = string.Empty;
    public DateTimeOffset WindowStart { get; init; }
    public double Score { get; init; }
    public string Decision { get; init; } = "monitor";
    public string Reasons { get; init; } = "[]";
}

/// <summary>Represents a point in the Arps decline forecast.</summary>
public sealed record ForecastPoint(DateOnly Date, double P10Bbl, double P50Bbl, double P90Bbl);

/// <summary>Forecast plus the fitted parameters, so the caller can audit the model.</summary>
public sealed record WellForecast(
    string WellId,
    string Name,
    double QiBpd,
    double DiPerDay,
    double RSquared,
    double MonthlyDeclinePct,
    int WindowsUsed,
    IReadOnlyList<ForecastPoint> Points,
    string ModelVersion);
