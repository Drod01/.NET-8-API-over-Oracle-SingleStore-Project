namespace Wellhead.Api.Data;

public enum SqlDialect
{
    Oracle,
    SingleStore
}

/// <summary>
/// The same logical operation expressed for two engines.
/// </summary>
/// <remarks>
/// Oracle and SingleStore (MemSQL) differ in bind-variable syntax, row limiting,
/// and upsert grammar. Keeping those differences in one auditable class - rather
/// than agenting string interpolation through repositories - is what makes a
/// two-database architecture maintainable. The repository stays dialect-agnostic;
/// the builder is unit tested for both engines.
/// </remarks>
public sealed class TelemetrySql
{
    private readonly SqlDialect _dialect;

    public TelemetrySql(SqlDialect dialect) => _dialect = dialect;

    public SqlDialect Dialect => _dialect;

    public string Bind(string name) => _dialect == SqlDialect.Oracle ? $":{name}" : $"@{name}";

    public string RowLimit(string name) =>
        _dialect == SqlDialect.Oracle ? $"FETCH FIRST {Bind(name)} ROWS ONLY" : $"LIMIT {Bind(name)}";

    public string SelectWindowColumns =>
        "well_id, window_start, window_end, sample_count, expected_samples, avg_flow_bbl, " +
        "total_volume_bbl, flow_cv, avg_pressure_psi, max_pressure_psi, avg_temp_f";

    public string LatestWindows =>
        "SELECT " + SelectWindowColumns + " FROM production_windows " +
        "WHERE well_id = " + Bind("wellId") + " ORDER BY window_start DESC " + RowLimit("rowLimit");

    public string FlaggedWindows =>
        "SELECT well_id, window_start, score, decision, reasons FROM anomaly_signals " +
        "WHERE decision = 'flag' AND window_start >= " + Bind("since") + " " +
        "ORDER BY window_start DESC " + RowLimit("rowLimit");

    public string WindowCount => "SELECT COUNT(*) FROM production_windows WHERE well_id = " + Bind("wellId");

    /// <summary>Oracle MERGE, keyed on the window's natural key.</summary>
    public string UpsertWindow => _dialect == SqlDialect.Oracle
        ? "MERGE INTO production_windows target " +
          "USING (SELECT " + Bind("wellId") + " AS well_id, " + Bind("windowStart") + " AS window_start FROM dual) source " +
          "ON (target.well_id = source.well_id AND target.window_start = source.window_start) " +
          "WHEN MATCHED THEN UPDATE SET " + UpdateAssignments("target") + " " +
          "WHEN NOT MATCHED THEN INSERT (" + SelectWindowColumns + ") VALUES (" +
          string.Join(", ", InsertBinds()) + ")"
        : "INSERT INTO production_windows (" + SelectWindowColumns + ") VALUES (" +
          string.Join(", ", InsertBinds()) + ") ON DUPLICATE KEY UPDATE " + UpdateAssignments("production_windows");

    private static readonly (string Column, string Parameter)[] UpdatableColumns =
    {
        ("sample_count", "sampleCount"),
        ("expected_samples", "expectedSamples"),
        ("avg_flow_bbl", "avgFlowBbl"),
        ("total_volume_bbl", "totalVolumeBbl"),
        ("flow_cv", "flowCv"),
        ("avg_pressure_psi", "avgPressurePsi"),
        ("max_pressure_psi", "maxPressurePsi"),
        ("avg_temp_f", "avgTempF")
    };

    private IEnumerable<string> InsertBinds() => new[]
    {
        "wellId", "windowStart", "windowEnd", "sampleCount", "expectedSamples", "avgFlowBbl",
        "totalVolumeBbl", "flowCv", "avgPressurePsi", "maxPressurePsi", "avgTempF"
    }.Select(Bind);

    private string UpdateAssignments(string prefix) => _dialect == SqlDialect.Oracle
        ? string.Join(", ", UpdatableColumns.Select(c => $"{prefix}.{c.Column} = {Bind(c.Parameter)}"))
        : string.Join(", ", UpdatableColumns.Select(c => $"{prefix}.{c.Column} = VALUES({c.Column})"));
}
