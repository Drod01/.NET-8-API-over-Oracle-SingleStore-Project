using Dapper;
using Wellhead.Api.Domain;

namespace Wellhead.Api.Data;

/// <summary>
/// Master data lives in Oracle: low volume, strongly relational, transactional,
/// and already the system of record for the asset hierarchy.
/// </summary>
public sealed class OracleWellRepository(IDbConnectionFactory connectionFactory) : IWellRepository
{
    private const string ListSql =
        "SELECT well_id, name, basin, county, status, spud_date FROM wells ORDER BY well_id";

    private const string GetSql =
        "SELECT well_id, name, basin, county, status, spud_date FROM wells WHERE well_id = :wellId";

    // Oracle MERGE keeps the upsert idempotent so a re-run of a load cannot
    // duplicate master data or trip the unique constraint.
    private const string UpsertSql =
        "MERGE INTO wells target " +
        "USING (SELECT :wellId AS well_id FROM dual) source " +
        "ON (target.well_id = source.well_id) " +
        "WHEN MATCHED THEN UPDATE SET target.name = :name, target.basin = :basin, " +
        "  target.county = :county, target.status = :status, target.spud_date = :spudDate " +
        "WHEN NOT MATCHED THEN INSERT (well_id, name, basin, county, status, spud_date) " +
        "VALUES (:wellId, :name, :basin, :county, :status, :spudDate)";

    public async Task<IReadOnlyList<Well>> ListWellsAsync(CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.Create();
        var rows = await connection.QueryAsync<Well>(
            new CommandDefinition(ListSql, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<Well?> GetWellAsync(string wellId, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.Create();
        return await connection.QuerySingleOrDefaultAsync<Well>(
            new CommandDefinition(GetSql, new { wellId }, cancellationToken: cancellationToken));
    }

    public async Task<int> UpsertWellsAsync(IReadOnlyList<Well> wells, CancellationToken cancellationToken)
    {
        if (wells.Count == 0)
        {
            return 0;
        }

        using var connection = connectionFactory.Create();
        using var transaction = connection.BeginTransaction();
        var affected = 0;
        foreach (var well in wells)
        {
            affected += await connection.ExecuteAsync(new CommandDefinition(
                UpsertSql,
                new
                {
                    wellId = well.WellId,
                    name = well.Name,
                    basin = well.Basin,
                    county = well.County,
                    status = well.Status,
                    spudDate = well.SpudDate?.ToDateTime(TimeOnly.MinValue)
                },
                transaction,
                cancellationToken: cancellationToken));
        }

        transaction.Commit();
        return affected;
    }
}
