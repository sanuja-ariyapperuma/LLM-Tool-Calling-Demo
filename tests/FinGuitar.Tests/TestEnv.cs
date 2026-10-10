using System.Security.Claims;
using FinGuitar.Web.Domain;
using FinGuitar.Web.Services;
using Microsoft.AspNetCore.Http;

namespace FinGuitar.Tests;

/// <summary>HttpContextAccessor keeps its context in an AsyncLocal, which a test factory method would lose.</summary>
public sealed class FixedAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }
}

/// <summary>The application services wired against a TestDb for one signed-in user, as DI would per request.</summary>
public sealed class TestEnv : IAsyncDisposable
{
    public TestDb Db { get; }
    public ChangeContext Context { get; } = new();
    public CurrentUser User { get; }
    public AuditService Audit { get; }
    public OrderService Orders { get; }
    public ProductService Products { get; }
    public CustomerService Customers { get; }
    public SalesService Sales { get; }

    // Seeded demo users: id 1 is Level1, id 2 is Level2.
    public const int Level1Id = 1;
    public const int Level2Id = 2;

    private TestEnv(TestDb db, int? userId)
    {
        Db = db;
        var accessor = new FixedAccessor { HttpContext = new DefaultHttpContext { User = Principal(userId) } };
        User = new CurrentUser(accessor);
        Audit = new AuditService(db.Db, db.Time, User, Context);
        Orders = new OrderService(db.Db, User, Audit);
        Products = new ProductService(db.Db, User, Audit);
        Customers = new CustomerService(db.Db);
        Sales = new SalesService(db.Db, db.Time);
    }

    public static async Task<TestEnv> CreateAsync(int? userId = Level1Id, DateTime? now = null)
        => new(await TestDb.CreateAsync(now, seed: true), userId);

    public static TestEnv ForUser(TestDb db, int? userId) => new(db, userId);

    private static ClaimsPrincipal Principal(int? userId)
    {
        if (userId is null) return new ClaimsPrincipal(new ClaimsIdentity());
        var (email, role) = userId == Level1Id ? ("level1@finguitar.local", Roles.Level1) : ("level2@finguitar.local", Roles.Level2);
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()!), new Claim(ClaimTypes.Name, email), new Claim(ClaimTypes.Role, role)],
            "test"));
    }

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
