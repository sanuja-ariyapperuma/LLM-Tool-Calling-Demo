using System.Security.Claims;
using FinGuitar.Web.Domain;

namespace FinGuitar.Web.Services;

/// <summary>The signed-in admin, read from the cookie claims. Write services call RequireLevel1 first (FR-A3).</summary>
public class CurrentUser(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public int UserId => int.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    public string Email => Principal?.FindFirstValue(ClaimTypes.Name) ?? "";
    public string Role => Principal?.FindFirstValue(ClaimTypes.Role) ?? "";
    public bool IsLevel1 => IsAuthenticated && Role == Roles.Level1;
    public bool IsAdmin => IsAuthenticated && (Role == Roles.Level1 || Role == Roles.Level2);

    public void RequireLevel1()
    {
        if (!IsLevel1) throw new ForbiddenException();
    }
}
