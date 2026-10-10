using System.Reflection;
using Dapper;

namespace FinGuitar.Web.Data;

/// <summary>Applies embedded Migrations/NNNN_name.sql scripts in order, each in its own transaction.</summary>
public sealed class DbMigrator(Db db, ILogger<DbMigrator>? logger = null)
{
    private const string Marker = ".Migrations.";

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        await using var conn = await db.OpenAsync(ct);

        await conn.ExecuteAsync("PRAGMA journal_mode = WAL;");
        await conn.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL)");

        var applied = (await conn.QueryAsync<int>("SELECT version FROM schema_version")).ToHashSet();

        var assembly = Assembly.GetExecutingAssembly();
        var scripts = assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) && n.Contains(Marker))
            .Select(n => (Name: n, File: n[(n.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..]))
            .OrderBy(s => s.File, StringComparer.Ordinal);

        foreach (var (name, file) in scripts)
        {
            var version = int.Parse(file[..4]);
            if (applied.Contains(version)) continue;

            using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
            var sql = await reader.ReadToEndAsync(ct);

            await using var tx = await conn.BeginTransactionAsync(ct);
            await conn.ExecuteAsync(sql, transaction: tx);
            await conn.ExecuteAsync(
                "INSERT INTO schema_version (version, applied_at) VALUES (@version, @at)",
                new { version, at = DapperSetup.FormatDate(DateTime.Now) }, tx);
            await tx.CommitAsync(ct);
            logger?.LogInformation("Applied migration {File}", file);
        }
    }
}
