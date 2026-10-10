using FinGuitar.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.Name = "finguitar.cart";
});
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new Db(builder.Configuration.GetConnectionString("Default")!));
builder.Services.AddSingleton<DbMigrator>();
builder.Services.AddSingleton<SeedData>();

var dataSource = new SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Default")).DataSource;
var dataDir = Path.GetDirectoryName(Path.GetFullPath(dataSource)) ?? ".";
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")))
    .SetApplicationName("FinGuitar");

var app = builder.Build();

// Migrate, then seed an empty database, before serving requests (D10).
Directory.CreateDirectory(dataDir);
await app.Services.GetRequiredService<DbMigrator>().MigrateAsync();
await app.Services.GetRequiredService<SeedData>().SeedIfEmptyAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// Inside Docker the app serves plain HTTP (spec 2.8.4).
if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.MapRazorPages();

app.Run();

public partial class Program;
