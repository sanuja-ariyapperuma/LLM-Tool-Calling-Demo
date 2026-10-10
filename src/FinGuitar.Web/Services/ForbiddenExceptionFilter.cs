using FinGuitar.Web.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FinGuitar.Web.Services;

/// <summary>
/// Turns a ForbiddenException from a write service (for example a direct POST by a Level2 user) into a 403.
/// The status-code pages middleware then shows the access-denied page (2.4).
/// </summary>
public class ForbiddenExceptionFilter : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var executed = await next();
        if (executed.Exception is ForbiddenException)
        {
            executed.ExceptionHandled = true;
            executed.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}
