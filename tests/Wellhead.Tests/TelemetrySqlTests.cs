using Wellhead.Api.Data;

namespace Wellhead.Api.Tests;

public class TelemetrySqlTests
{
    private static readonly TelemetrySql Oracle = new(SqlDialect.Oracle);
    private static readonly TelemetrySql SingleStore = new(SqlDialect.SingleStore);

    [Fact]
    public void Oracle_limits_rows_with_fetch_first_and_bind_syntax()
    {
        var sql = Oracle.LatestWindows;

        Assert.Contains("FETCH FIRST :rowLimit ROWS ONLY", sql);
        Assert.Contains("well_id = :wellId", sql);
        Assert.DoesNotContain("@", sql);
        Assert.DoesNotContain("LIMIT", sql);
    }

    [Fact]
    public void SingleStore_limits_rows_with_limit_and_at_bind_syntax()
    {
        var sql = SingleStore.LatestWindows;

        Assert.Contains("LIMIT @rowLimit", sql);
        Assert.Contains("well_id = @wellId", sql);
        Assert.DoesNotContain(":", sql);
    }

    [Fact]
    public void Oracle_upsert_uses_merge_and_bound_updates()
    {
        var sql = Oracle.UpsertWindow;

        Assert.Contains("MERGE INTO production_windows target", sql);
        Assert.Contains("WHEN MATCHED THEN UPDATE SET", sql);
        Assert.Contains("WHEN NOT MATCHED THEN INSERT", sql);
        Assert.Contains("target.sample_count = :sampleCount", sql);
        Assert.Contains("target.avg_flow_bbl = :avgFlowBbl", sql);
        Assert.DoesNotContain("VALUES(sample_count)", sql);
        Assert.DoesNotContain("@", sql);
    }

    [Fact]
    public void SingleStore_upsert_uses_on_duplicate_key_update()
    {
        var sql = SingleStore.UpsertWindow;

        Assert.Contains("ON DUPLICATE KEY UPDATE", sql);
        Assert.Contains("production_windows.sample_count = VALUES(sample_count)", sql);
        Assert.Contains("@wellId", sql);
        Assert.DoesNotContain("MERGE", sql);
    }

    [Fact]
    public void Both_dialects_select_the_same_columns()
    {
        foreach (var sql in new[] { Oracle.LatestWindows, SingleStore.LatestWindows })
        {
            foreach (var column in new[]
                     {
                         "well_id", "window_start", "window_end", "sample_count", "expected_samples",
                         "avg_flow_bbl", "total_volume_bbl", "flow_cv", "avg_pressure_psi",
                         "max_pressure_psi", "avg_temp_f"
                     })
            {
                Assert.Contains(column, sql);
            }
        }
    }

    [Fact]
    public void Alerts_query_only_reads_flags()
    {
        Assert.Contains("decision = 'flag'", Oracle.FlaggedWindows);
        Assert.Contains("decision = 'flag'", SingleStore.FlaggedWindows);
        Assert.Contains("window_start >= :since", Oracle.FlaggedWindows);
        Assert.Contains("window_start >= @since", SingleStore.FlaggedWindows);
    }

    [Fact]
    public void Bind_prefix_matches_the_dialect()
    {
        Assert.Equal(":value", Oracle.Bind("value"));
        Assert.Equal("@value", SingleStore.Bind("value"));
        Assert.Equal(SqlDialect.Oracle, Oracle.Dialect);
    }
}
