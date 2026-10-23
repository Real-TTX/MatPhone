using System.ComponentModel.DataAnnotations;
using MatPhone.Configuration;
using MatPhone.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatPhone.Pages.Admin;

/// <summary>Sign-in rules, how many failed SIP authentications an address gets, and the address the installation is reached at (for the links that are handed on).</summary>
public sealed class SecurityModel(ConfigStore config, ActivityLogger log, PublicUrl urls) : PageModelBase
{
    public sealed class InputModel
    {
        [Range(1, 365, ErrorMessage = "Admin.Security.SessionDays.Range")]
        [Display(Name = "Admin.Security.SessionDays")]
        public int SessionDays { get; set; } = 14;

        public bool RequireTotpForAdmins { get; set; }

        [Range(3, 100, ErrorMessage = "Admin.Security.SipMaxFailures.Range")]
        [Display(Name = "Admin.Security.SipMaxFailures")]
        public int SipMaxFailures { get; set; } = 10;

        [Display(Name = "Admin.Security.PublicUrl")]
        public string? PublicUrl { get; set; }

        public bool ForceSecureCookie { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    /// <summary>The address that is used now, so that the field can say what happens when it is empty.</summary>
    public string CurrentBase => urls.Absolute("/");

    public bool UrlFromEnvironment => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MATPHONE__Server__PublicUrl"));

    public void OnGet()
    {
        Prepare();
        var s = config.Current.Security;
        Input = new InputModel
        {
            SessionDays = s.SessionDays, RequireTotpForAdmins = s.RequireTotpForAdmins,
            SipMaxFailures = s.SipMaxFailures, PublicUrl = config.Current.Server.PublicUrl, ForceSecureCookie = config.Current.Server.ForceSecureCookie,
        };
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Prepare();
        var url = Input.PublicUrl?.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(url) && !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
        {
            ModelState.AddModelError("Input.PublicUrl", L["Admin.Security.PublicUrl.Invalid"]);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        config.Update(c =>
        {
            var s = c.Security;
            s.SessionDays = Input.SessionDays;
            s.RequireTotpForAdmins = Input.RequireTotpForAdmins;
            s.SipMaxFailures = Input.SipMaxFailures;
            c.Server.PublicUrl = url ?? "";
            c.Server.ForceSecureCookie = Input.ForceSecureCookie;
        });
        await log.LogAsync(L["Admin.Security.Log.Changed"], "Security");
        Flash(L["Admin.Security.Saved"]);
        return RedirectToPage();
    }

    private void Prepare()
    {
        Head(L["Admin.Security.Title"], L["Admin.Security.Sub"], (L["Admin.Settings"], null), (L["Admin.Security.Title"], null));
        ViewData["Width"] = "narrow";
    }
}
