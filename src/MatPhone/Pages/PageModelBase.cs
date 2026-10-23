using MatPhone.Localization;
using MatPhone.UiKit;
using MatPhone.Business;
using MatPhone.Services;
using Microsoft.Extensions.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MatPhone.Pages;

/// <summary>What every page of the application needs: the title and the breadcrumb for the layout, and the messages that survive a redirect.</summary>
public abstract class PageModelBase : PageModel
{
    /// <summary>The texts of the interface in the language of the request: <c>L["Groups.Title"]</c>, <c>L["Groups.Saved", name]</c>.</summary>
    protected IStringLocalizer L => Text.L;

    /// <summary>Who acts, for the business layer (which checks the permissions itself).</summary>
    protected Actor Me => Actor.From(HttpContext.RequestServices.GetRequiredService<ICurrentUser>());

    /// <summary>The errors of a save of the business layer, at their fields ("Input.Name"); one without a field goes to the summary.</summary>
    protected void AddErrors(SaveResult result, string prefix = "Input")
    {
        foreach (var (field, message) in result.Errors)
        {
            ModelState.AddModelError(field.Length == 0 ? string.Empty : prefix + "." + field, message);
        }
    }

    protected void Head(string title, string? sub = null, params (string Text, string? Url)[] crumbs)
    {
        ViewData["Title"] = title;
        ViewData["PageSub"] = sub;
        ViewData["Crumbs"] = Crumbs.Of(crumbs.Length == 0 ? [(title, null)] : crumbs);
    }

    /// <summary>A message after a save: the page redirects and the layout shows it as a toast.</summary>
    protected void Flash(string message) => TempData["Flash"] = message;

    protected void FlashError(string message) => TempData["Error"] = message;

    /// <summary>A local address only: a "return to" that comes from the outside must never send the user to another site.</summary>
    protected string SafeReturn(string? returnUrl, string fallback = "/")
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : fallback;
}
