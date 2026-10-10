using FinGuitar.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace FinGuitar.Tests;

/// <summary>
/// A fresh in-memory SQLite database per test. One keeper connection stays open so the database survives,
/// and DbMigrator builds the schema, so tests run the same SQL scripts as the app.
/// </summary>
public sealed class TestDb : IAsyncDisposable
{
    private readonly SqliteConnection _keeper;

    public Db Db { get; }
    public string ConnectionString { get; }
    public FakeTimeProvider Time { get; }

    private TestDb(FakeTimeProvider time)
    {
        ConnectionString = $"Data Source=file:test-{Guid.NewGuid():N}?mode=memory&cache=shared";
        _keeper = new SqliteConnection(ConnectionString);
        _keeper.Open();
        Db = new Db(ConnectionString);
        Time = time;
    }

    /// <summary>Creates a migrated database. Fake time starts at Friday 2026-10-09 at noon unless given.</summary>
    public static async Task<TestDb> CreateAsync(DateTime? now = null, bool seed = false)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(now ?? new DateTime(2026, 10, 9, 12, 0, 0), TimeSpan.Zero));
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        var db = new TestDb(time);
        await new DbMigrator(db.Db).MigrateAsync();
        if (seed) await new SeedData(db.Db, time).SeedIfEmptyAsync();
        return db;
    }

    public async ValueTask DisposeAsync() => await _keeper.DisposeAsync();
}
