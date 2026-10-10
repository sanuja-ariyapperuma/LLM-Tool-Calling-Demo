using Microsoft.Data.Sqlite;

namespace FinGuitar.Web.Data;

/// <summary>Holds the connection string and opens connections. Each service call opens and disposes its own.</summary>
public sealed class Db
{
    private readonly string _connectionString;

    public Db(string connectionString)
    {
        _connectionString = connectionString;
        DapperSetup.Configure();
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await cmd.ExecuteNonQueryAsync(ct);
        return conn;
    }
}
