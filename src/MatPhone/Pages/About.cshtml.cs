using System.Reflection;

namespace MatPhone.Pages;

/// <summary>About MatPhone (reached through the version at the foot of the menu): the version, the parts it is made of and their licences.</summary>
public sealed class AboutModel(IWebHostEnvironment env) : PageModelBase
{
    public string Toolkit { get; private set; } = "";

    public string Sip { get; } = typeof(SIPSorcery.SIP.SIPTransport).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";

    public void OnGet()
    {
        Head(L["About.Title"], null, (L["About.Title"], null));
        ViewData["Width"] = "narrow";
        // The first line of the copied toolkit names its version: "/* MatUi 0.9.2 (hash) ...".
        var css = Path.Combine(env.WebRootPath, "ui", "matui.css");
        if (System.IO.File.Exists(css))
        {
            using var reader = new StreamReader(css);
            var first = reader.ReadLine() ?? "";
            var parts = first.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Toolkit = parts.Length > 2 ? parts[2] : "";
        }
    }
}
