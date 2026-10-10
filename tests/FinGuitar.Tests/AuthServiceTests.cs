using System.Security.Claims;
using FinGuitar.Web.Data;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Http;

namespace FinGuitar.Tests;

public class AuthServiceTests
{
    private static AuthService Auth(TestDb db) => new(db.Db, db.Time, new HttpContextAccessor());

    [Fact]
    public async Task Correct_password_signs_in_with_id_email_and_role_claims()
    {
        await using var db = await TestDb.CreateAsync(seed: true);
        var r = await Auth(db).AuthenticateAsync("level1@finguitar.local", SeedData.Level1Password);

        Assert.True(r.Succeeded);
        Assert.Equal("Level1", r.Principal!.FindFirstValue(ClaimTypes.Role));
        Assert.Equal("level1@finguitar.local", r.Principal.FindFirstValue(ClaimTypes.Name));
        Assert.Equal("1", r.Principal.FindFirstValue(ClaimTypes.NameIdentifier));

        var l2 = await Auth(db).AuthenticateAsync("LEVEL2@finguitar.local", SeedData.Level2Password); // case-insensitive email
        Assert.Equal("Level2", l2.Principal!.FindFirstValue(ClaimTypes.Role));
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_give_the_same_message()
    {
        await using var db = await TestDb.CreateAsync(seed: true);
        var wrong = await Auth(db).AuthenticateAsync("level1@finguitar.local", "nope");
        var unknown = await Auth(db).AuthenticateAsync("nobody@finguitar.local", "nope");

        Assert.False(wrong.Succeeded);
        Assert.Equal("Invalid email or password.", wrong.Error);
        Assert.Equal(wrong.Error, unknown.Error);
    }

    [Fact]
    public async Task Fifth_failure_locks_the_account_and_it_unlocks_after_five_minutes()
    {
        await using var db = await TestDb.CreateAsync(seed: true);
        var auth = Auth(db);

        for (var i = 0; i < 4; i++)
            Assert.Equal(AuthService.InvalidMessage, (await auth.AuthenticateAsync("level1@finguitar.local", "bad")).Error);
        Assert.Equal(AuthService.InvalidMessage, (await auth.AuthenticateAsync("level1@finguitar.local", "bad")).Error); // 5th

        // Locked: even the right password is refused.
        var locked = await auth.AuthenticateAsync("level1@finguitar.local", SeedData.Level1Password);
        Assert.False(locked.Succeeded);
        Assert.Equal(AuthService.LockedMessage, locked.Error);

        db.Time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(AuthService.LockedMessage, (await auth.AuthenticateAsync("level1@finguitar.local", SeedData.Level1Password)).Error);

        db.Time.Advance(TimeSpan.FromMinutes(1.1));
        Assert.True((await auth.AuthenticateAsync("level1@finguitar.local", SeedData.Level1Password)).Succeeded);
    }

    [Fact]
    public async Task A_success_resets_the_failure_count()
    {
        await using var db = await TestDb.CreateAsync(seed: true);
        var auth = Auth(db);

        for (var i = 0; i < 4; i++) await auth.AuthenticateAsync("level2@finguitar.local", "bad");
        Assert.True((await auth.AuthenticateAsync("level2@finguitar.local", SeedData.Level2Password)).Succeeded);

        // 4 more failures must not lock: the count started again from 0.
        for (var i = 0; i < 4; i++) await auth.AuthenticateAsync("level2@finguitar.local", "bad");
        Assert.True((await auth.AuthenticateAsync("level2@finguitar.local", SeedData.Level2Password)).Succeeded);
    }
}
