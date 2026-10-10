using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.RegularExpressions;
using Dapper;
using FinGuitar.Web.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;

namespace FinGuitar.Tests;

/// <summary>The real app on a throwaway SQLite file, seeded at startup like a first run.</summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "finguitar-test-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dir);
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(_dir, "test.db")};Pooling=False");
        builder.UseEnvironment("Production");
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task<T> QueryAsync<T>(string sql, object? args = null)
    {
        await using var conn = await Services.GetRequiredService<Db>().OpenAsync();
        return await conn.ExecuteScalarAsync<T>(sql, args) ?? default!;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(_dir, true); } catch (IOException) { /* temp files; best effort */ }
    }
}

public static class HttpExtensions
{
    private static readonly Regex Token = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>Loads a page and returns the anti-forgery token from its first form (the cookie half stays in the client).</summary>
    public static async Task<string> GetTokenAsync(this HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        var m = Token.Match(html);
        Assert.True(m.Success, $"no anti-forgery token on {url}");
        return WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient client, string url, string token, params (string Key, string Value)[] fields)
    {
        var data = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();
        data.Add(new("__RequestVerificationToken", token));
        return await client.PostAsync(url, new FormUrlEncodedContent(data));
    }

    public static async Task LoginAsync(this HttpClient client, string email, string password)
    {
        var token = await client.GetTokenAsync("/Account/Login");
        var res = await client.PostFormAsync("/Account/Login", token, ("Email", email), ("Password", password));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
    }

    public static Task LoginLevel1Async(this HttpClient c) => c.LoginAsync("level1@finguitar.local", SeedData.Level1Password);
    public static Task LoginLevel2Async(this HttpClient c) => c.LoginAsync("level2@finguitar.local", SeedData.Level2Password);
}
