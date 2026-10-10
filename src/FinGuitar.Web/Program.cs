using FinGuitar.Web.Chat;
using FinGuitar.Web.Data;
using Microsoft.Extensions.AI;
using System.ClientModel;
using OllamaSharp;
using OpenAI;
using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Admin", "Admin");
    o.Conventions.AuthorizePage("/Admin/Products/Create", "Level1");
    o.Conventions.AuthorizePage("/Admin/Products/Edit", "Level1");
}).AddMvcOptions(o => o.Filters.Add<ForbiddenExceptionFilter>());

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.Name = "finguitar.cart";
});
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "finguitar.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/AccessDenied";
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", p => p.RequireRole(Roles.Level1, Roles.Level2))
    .AddPolicy("Level1", p => p.RequireRole(Roles.Level1));

builder.Services.AddSingleton(new Db(builder.Configuration.GetConnectionString("Default")!));
builder.Services.AddSingleton<DbMigrator>();
builder.Services.AddSingleton<SeedData>();

builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<ChangeContext>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<SalesService>();
builder.Services.AddScoped<CartService>();

// The chat: a model client with automatic tool calling, and the chat services.
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("Llm"));
builder.Services.AddChatClient(sp =>
{
    var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmOptions>>().Value;
    // The per-turn timeout (Llm:TimeoutSeconds) is applied by ChatService, so the clients' own limits are generous.
    var networkTimeout = TimeSpan.FromMinutes(15);
    switch (o.Provider)
    {
        case LlmOptions.Ollama:
            return new OllamaApiClient(new HttpClient { BaseAddress = new Uri(o.Endpoint), Timeout = networkTimeout }, o.Model);
        case LlmOptions.OpenAI:
            if (string.IsNullOrWhiteSpace(o.ApiKey))
                throw new InvalidOperationException("Llm:ApiKey is required when Llm:Provider is OpenAI (set Llm__ApiKey).");
            var client = new OpenAIClient(new ApiKeyCredential(o.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(o.Endpoint), NetworkTimeout = networkTimeout });
            return client.GetChatClient(o.Model).AsIChatClient();
        default:
            throw new InvalidOperationException($"Llm:Provider must be '{LlmOptions.Ollama}' or '{LlmOptions.OpenAI}', not '{o.Provider}'.");
    }
}).UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = ChatService.MaxToolRounds);
builder.Services.AddScoped<ChatStore>();
builder.Services.AddScoped<ToolResultLog>();
builder.Services.AddScoped<ProposedActionService>();
builder.Services.AddScoped<ChatTools>();
builder.Services.AddScoped<ChatToolCatalog>();
builder.Services.AddScoped<ChatService>();

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
app.UseStatusCodePagesWithReExecute("/Error/{0}");

// Inside Docker the app serves plain HTTP (spec 2.8.4).
if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
{
    app.UseHttpsRedirection();
}

app.UseRequestLocalization("en-US");
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();

public partial class Program;
