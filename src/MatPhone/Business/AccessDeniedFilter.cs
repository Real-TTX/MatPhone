using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MatPhone.Business;

/// <summary>A refusal of the business layer is a 403 page, not an error: the page did not hide a control it should have, or somebody posted by hand.</summary>
public sealed class AccessDeniedFilter : IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var executed = await next();
        if (executed.Exception is AccessDeniedException)
        {
            executed.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            executed.ExceptionHandled = true;
        }
    }
}
