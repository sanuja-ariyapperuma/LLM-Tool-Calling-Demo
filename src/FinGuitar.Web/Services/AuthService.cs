using System.Security.Claims;
using Dapper;
using FinGuitar.Web.Data;
using FinGuitar.Web.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace FinGuitar.Web.Services;

public record AuthResult(ClaimsPrincipal? Principal, string? Error)
{
    public bool Succeeded => Principal is not null;
}

/// <summary>Cookie sign-in with salted password hashes, lockout after 5 failures, and one message for wrong email or password.</summary>
public class AuthService(Db db, TimeProvider time, IHttpContextAccessor accessor)
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
    public const string InvalidMessage = "Invalid email or password.";
    public const string LockedMessage = "Too many attempts. Try again in a few minutes.";

    private static readonly PasswordHasher<AdminUser> Hasher = new();
    private static readonly AdminUser DummyUser = new() { Email = "nobody@invalid" };
    private static readonly string DummyHash = Hasher.HashPassword(DummyUser, "not-a-real-password");

    public async Task<AuthResult> AuthenticateAsync(string email, string password)
    {
        await using var conn = await db.OpenAsync();
        var user = await conn.QuerySingleOrDefaultAsync<AdminUser>(
            "SELECT id, email, password_hash, role, failed_attempts, lockout_until FROM admin_users WHERE email = @email",
            new { email = email.Trim() });

        if (user is null)
        {
            Hasher.VerifyHashedPassword(DummyUser, DummyHash, password); // similar timing to a wrong password
            return new AuthResult(null, InvalidMessage);
        }

        var now = time.GetLocalNow().DateTime;
        if (user.LockoutUntil is { } until && until > now)
            return new AuthResult(null, LockedMessage);

        var verdict = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verdict == PasswordVerificationResult.Failed)
        {
            var attempts = user.FailedAttempts + 1;
            if (attempts >= MaxFailedAttempts)
                await conn.ExecuteAsync("UPDATE admin_users SET failed_attempts = 0, lockout_until = @until WHERE id = @id",
                    new { id = user.Id, until = now + LockoutDuration });
            else
                await conn.ExecuteAsync("UPDATE admin_users SET failed_attempts = @attempts WHERE id = @id",
                    new { id = user.Id, attempts });
            return new AuthResult(null, InvalidMessage);
        }

        await conn.ExecuteAsync(
            "UPDATE admin_users SET failed_attempts = 0, lockout_until = NULL WHERE id = @id", new { id = user.Id });
        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
            await conn.ExecuteAsync("UPDATE admin_users SET password_hash = @hash WHERE id = @id",
                new { id = user.Id, hash = Hasher.HashPassword(user, password) });

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        return new AuthResult(new ClaimsPrincipal(identity), null);
    }

    public async Task<string?> SignInAsync(string email, string password)
    {
        var result = await AuthenticateAsync(email, password);
        if (!result.Succeeded) return result.Error;
        await accessor.HttpContext!.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, result.Principal!);
        return null;
    }

    public Task SignOutAsync() =>
        accessor.HttpContext!.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}
