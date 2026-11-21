namespace Wellhead.Api.Domain;

/// <summary>
/// One closed five-minute aggregation window for a well.
/// </summary>
/// <remarks>
/// Shapes deliberately mirror the SingleStore columnstore table so Dapper can
/// materialise rows without a mapping layer, and so the API contract, the
/// warehouse row and the streaming aggregate are all the same concept.
/// </remarks>
public sealed class ProductionWindow
{
    public string WellId { get; init; } = string.Empty;
    public DateTimeOffset WindowStart { get; init; }
    public DateTimeOffset WindowEnd { get; init; }
    public int SampleCount { get; init; }
    public int ExpectedSamples { get; init; }
    public double AvgFlowBbl { get; init; }
    public double TotalVolumeBbl { get; init; }
    public double FlowCv { get; init; }
    public double AvgPressurePsi { get; init; }
    public double MaxPressurePsi { get; init; }
    public double AvgTempF { get; init; }

    public double CoverageRatio =>
        ExpectedSamples <= 0 ? 0d : Math.Min(1d, (double)SampleCount / ExpectedSamples);

    public double WindowHours => Math.Max(0d, (WindowEnd - WindowStart).TotalHours);

    /// <summary>Volume extrapolated to a full day at the window's average rate.</summary>
    public double DailyRateBbl => WindowHours <= 0 ? 0d : TotalVolumeBbl / (WindowHours / 24d);

    public bool IsComplete => CoverageRatio >= 0.8d;
}
