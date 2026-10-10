using Dapper;
using FinGuitar.Web.Data;
using Microsoft.Data.Sqlite;

namespace FinGuitar.Tests;

public class MigrationTests
{
    [Fact]
    public async Task Empty_database_migrates_to_latest_and_second_run_changes_nothing()
    {
        await using var t = await TestDb.CreateAsync();
        await using var conn = await t.Db.OpenAsync();

        var first = (await conn.QueryAsync<int>("SELECT version FROM schema_version ORDER BY version")).ToList();
        Assert.Equal(1, first[0]);

        await new DbMigrator(t.Db).MigrateAsync();

        var second = (await conn.QueryAsync<int>("SELECT version FROM schema_version ORDER BY version")).ToList();
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Foreign_keys_are_enforced()
    {
        await using var t = await TestDb.CreateAsync();
        await using var conn = await t.Db.OpenAsync();

        await Assert.ThrowsAsync<SqliteException>(() => conn.ExecuteAsync(
            "INSERT INTO orders (customer_id, order_date, status, total_cents) VALUES (999, '2026-10-09 10:00:00', 'Pending', 100)"));
    }

    [Fact]
    public async Task Audit_entries_refuse_update_and_delete()
    {
        await using var t = await TestDb.CreateAsync();
        await using var conn = await t.Db.OpenAsync();
        await conn.ExecuteAsync(
            @"INSERT INTO audit_entries (occurred_at, user_id, user_email, user_role, source, entity_type, entity_id, action, summary, changes_json)
              VALUES ('2026-10-09 10:00:00', 1, 'a@b.c', 'Level1', 'AdminPanel', 'Order', 1, 'OrderStatusChanged', 's', '[]')");

        var upd = await Assert.ThrowsAsync<SqliteException>(() => conn.ExecuteAsync("UPDATE audit_entries SET summary = 'x'"));
        Assert.Contains("append-only", upd.Message);
        var del = await Assert.ThrowsAsync<SqliteException>(() => conn.ExecuteAsync("DELETE FROM audit_entries"));
        Assert.Contains("append-only", del.Message);
    }
}
