using System.ComponentModel.DataAnnotations;
using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Services;
using Microsoft.AspNetCore.Mvc;

namespace MatPhone.Pages.Admin;

/// <summary>
/// The settings of the telephony that are not data: where the engine listens and what address it gives out, how numbers are read, the feature codes. A change of
/// the ports or the address restarts the engine (calls in progress end).
/// </summary>
public sealed class TelephonyModel(ConfigStore config, ActivityLogger log) : PageModelBase
{
    public sealed class InputModel
    {
        [StringLength(253, ErrorMessage = "Common.Validation.TooLong")]
        public string? PublicAddress { get; set; }

        [Range(1, 65535, ErrorMessage = "Common.Validation.Port")]
        public int SipPort { get; set; } = 5060;

        [Range(1024, 65535, ErrorMessage = "Common.Validation.Port")]
        public int RtpPortFrom { get; set; } = 6100;

        [Range(1024, 65535, ErrorMessage = "Common.Validation.Port")]
        public int RtpPortTo { get; set; } = 6199;

        public bool SipTrace { get; set; }

        [Required(ErrorMessage = "Telephony.CountryCode.Required")]
        [RegularExpression("^[1-9][0-9]{0,3}$", ErrorMessage = "Telephony.CountryCode.Invalid")]
        public string CountryCode { get; set; } = "49";

        [RegularExpression("^0?[1-9][0-9]{0,5}$", ErrorMessage = "Telephony.AreaCode.Invalid")]
        public string? AreaCode { get; set; }

        [RegularExpression("^[0-9]{0,2}$", ErrorMessage = "Telephony.OutsideLinePrefix.Invalid")]
        public string? OutsideLinePrefix { get; set; } = "0";

        [Range(5, 120, ErrorMessage = "Telephony.NoAnswer.Range")]
        public int DefaultRingSeconds { get; set; } = 20;

        [RegularExpression("^\\*[0-9]{1,4}$", ErrorMessage = "Telephony.Code.Invalid")]
        public string MailboxCode { get; set; } = "*86";

        [RegularExpression("^\\*[0-9]{1,4}$", ErrorMessage = "Telephony.Code.Invalid")]
        public string DoNotDisturbCode { get; set; } = "*78";

        [RegularExpression("^\\*[0-9]{1,4}$", ErrorMessage = "Telephony.Code.Invalid")]
        public string GroupToggleCode { get; set; } = "*85";

        [Range(60, 3600, ErrorMessage = "Telephony.MaxRegisterSeconds.Range")]
        public int MaxRegisterSeconds { get; set; } = 600;

        [StringLength(100, ErrorMessage = "Common.Validation.TooLong")]
        public string? CompanyName { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet()
    {
        Me.RequireAdmin();
        var t = config.Current.Telephony;
        Input = new InputModel
        {
            PublicAddress = t.PublicAddress, SipPort = t.SipPort, RtpPortFrom = t.RtpPortFrom, RtpPortTo = t.RtpPortTo, SipTrace = t.SipTrace, CountryCode = t.CountryCode,
            AreaCode = t.AreaCode, OutsideLinePrefix = t.OutsideLinePrefix, DefaultRingSeconds = t.DefaultRingSeconds, MailboxCode = t.MailboxCode,
            DoNotDisturbCode = t.DoNotDisturbCode, GroupToggleCode = t.GroupToggleCode, MaxRegisterSeconds = t.MaxRegisterSeconds, CompanyName = config.Current.Company.Name,
        };
        Prepare();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Me.RequireAdmin();
        Prepare();
        if (Input.RtpPortTo - Input.RtpPortFrom < 10)
        {
            ModelState.AddModelError("Input.RtpPortTo", L["Telephony.Rtp.TooFew"]);
        }

        if (Input.RtpPortFrom <= Input.SipPort && Input.SipPort <= Input.RtpPortTo)
        {
            ModelState.AddModelError("Input.SipPort", L["Telephony.SipPort.InRange"]);
        }

        var codes = new[] { Input.MailboxCode, Input.DoNotDisturbCode, Input.GroupToggleCode };
        if (codes.Distinct().Count() != codes.Length)
        {
            ModelState.AddModelError("Input.GroupToggleCode", L["Telephony.Code.Twice"]);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        config.Update(c =>
        {
            var t = c.Telephony;
            t.PublicAddress = Input.PublicAddress?.Trim() ?? "";
            t.SipPort = Input.SipPort;
            t.RtpPortFrom = Input.RtpPortFrom;
            t.RtpPortTo = Input.RtpPortTo;
            t.SipTrace = Input.SipTrace;
            t.CountryCode = Input.CountryCode.Trim();
            t.AreaCode = (Input.AreaCode ?? "").Trim().TrimStart('0');
            t.OutsideLinePrefix = Input.OutsideLinePrefix?.Trim() ?? "";
            t.DefaultRingSeconds = Input.DefaultRingSeconds;
            t.MailboxCode = Input.MailboxCode.Trim();
            t.DoNotDisturbCode = Input.DoNotDisturbCode.Trim();
            t.GroupToggleCode = Input.GroupToggleCode.Trim();
            t.MaxRegisterSeconds = Input.MaxRegisterSeconds;
            c.Company.Name = Input.CompanyName?.Trim() ?? "";
        });
        TelephonySignals.Notify(TelephonyChange.Settings);
        await log.LogAsync(L["Telephony.Log.Changed"], "Telephony");
        Flash(L["Telephony.Saved"]);
        return RedirectToPage();
    }

    private void Prepare()
    {
        Head(L["Telephony.Title"], L["Telephony.Sub"], (L["Admin.Settings"], null), (L["Telephony.Title"], null));
        ViewData["Width"] = "narrow";
    }
}
