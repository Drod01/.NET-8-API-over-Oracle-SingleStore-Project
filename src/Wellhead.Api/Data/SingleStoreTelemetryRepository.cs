using Dapper;
using Wellhead.Api.Domain;

namespace Wellhead.Api.Data;

/// <summary>
/// High-volume telemetry lives in SingleStore (MemSQL): columnstore compression
/// over billions of five-minute rows, sharded by well so a well's history is
/// co-located, with sub-second scans for KPI queries.
/// </summary>
public sealed class SingleStoreTelemetryRepository(IDbConnectionFactory connectionFactory) : ITelemetryRepository
{
    private readonly TelemetrySql _sql = new(SqlDialect.SingleStore);

    public async Task<IReadOnlyList<ProductionWindow>> LatestWindowsAsync(
        string wellId,
        int limit,
        CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.Create();
        var rows = await connection.QueryAsync<ProductionWindow>(new CommandDefinition(
            _sql.LatestWindows,
            new { wellId, rowLimit = Math.Clamp(limit, 1, 5_000) },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<FlaggedWindow>> FlaggedWindowsAsync(
        DateTimeOffset since,
        int limit,
        CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.Create();
        var rows = await connection.QueryAsync<FlaggedWindow>(new CommandDefinition(
            _sql.FlaggedWindows,
            new { since = since.UtcDateTime, rowLimit = Math.Clamp(limit, 1, 1_000) },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<int> UpsertWindowsAsync(
        IReadOnlyList<ProductionWindow> windows,
        CancellationToken cancellationToken)
    {
        if (windows.Count == 0)
        {
            return 0;
        }

        using var connection = connectionFactory.Create();
        using var transaction = connection.BeginTransaction();
        var affected = 0;
        foreach (var window in windows)
        {
            affected += await connection.ExecuteAsync(new CommandDefinition(
                _sql.UpsertWindow,
                new
                {
                    wellId = window.WellId,
                    windowStart = window.WindowStart.UtcDateTime,
                    windowEnd = window.WindowEnd.UtcDateTime,
                    sampleCount = window.SampleCount,
                    expectedSamples = window.ExpectedSamples,
                    avgFlowBbl = window.AvgFlowBbl,
                    totalVolumeBbl = window.TotalVolumeBbl,
                    flowCv = window.FlowCv,
                    avgPressurePsi = window.AvgPressurePsi,
                    maxPressurePsi = window.MaxPressurePsi,
                    avgTempF = window.AvgTempF
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        transaction.Commit();
        return affected;
    }

    public async Task<int> CountWindowsAsync(string wellId, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.Create();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            _sql.WindowCount,
            new { wellId },
            cancellationToken: cancellationToken));
    }
}
