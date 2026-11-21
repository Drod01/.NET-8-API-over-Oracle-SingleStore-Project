using System.Data;
using Wellhead.Api.Domain;

namespace Wellhead.Api.Data;

public interface IDbConnectionFactory
{
    IDbConnection Create();
}

public sealed class OracleConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public IDbConnection Create() => new Oracle.ManagedDataAccess.Client.OracleConnection(connectionString);
}

public sealed class SingleStoreConnectionFactory(string connectionString) : IDbConnectionFactory
{
    // SingleStore speaks the MySQL wire protocol, so MySqlConnector is the client.
    public IDbConnection Create() => new MySqlConnector.MySqlConnection(connectionString);
}

public interface IWellRepository
{
    Task<IReadOnlyList<Well>> ListWellsAsync(CancellationToken cancellationToken);

    Task<Well?> GetWellAsync(string wellId, CancellationToken cancellationToken);

    Task<int> UpsertWellsAsync(IReadOnlyList<Well> wells, CancellationToken cancellationToken);
}

public interface ITelemetryRepository
{
    Task<IReadOnlyList<ProductionWindow>> LatestWindowsAsync(string wellId, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<FlaggedWindow>> FlaggedWindowsAsync(DateTimeOffset since, int limit, CancellationToken cancellationToken);

    Task<int> UpsertWindowsAsync(IReadOnlyList<ProductionWindow> windows, CancellationToken cancellationToken);

    Task<int> CountWindowsAsync(string wellId, CancellationToken cancellationToken);
}
