using System.Globalization;
using MatPhone.Business;
using MatPhone.Configuration;
using MatPhone.Data;
using MatPhone.Data.Entities;
using MatPhone.Localization;
using MatPhone.Services;
using MatPhone.Services.Auth;
using MatPhone.Services.Backup;
using MatPhone.Storage;
using MatPhone.Telephony;
using MatPhone.UiKit;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

// The image checks itself (HEALTHCHECK in the Dockerfile): it needs no curl.
if (args.Contains("--healthcheck"))
{
    return await HealthCheck.RunAsync();
}

var dataDir = Environment.GetEnvironmentVariable("MATPHONE_DATA") is { Length: > 0 } fromEnv
    ? fromEnv
    : Path.Combine(Directory.GetCurrentDirectory(), "data");
Directory.CreateDirectory(dataDir);
var config = new ConfigStore(dataDir);
Fmt.Configure(config.Current.Server.TimeZone);

var builder = WebApplication.CreateBuilder(args);

// Port 6050; ASPNETCORE_URLS still wins (tests, a reverse proxy on the same machine).
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls($"http://+:{Environment.GetEnvironmentVariable("MATPHONE_PORT") ?? "6050"}");
}

builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 50L * 1024 * 1024);
// SIPSorcery logs through Microsoft.Extensions.Logging: only its warnings, it is chatty.
builder.Logging.AddFilter("SIPSorcery", LogLevel.Warning);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

// Sessions live in the database; the keys that protect the cookie (and secrets) live in the data volume, so a restart keeps everybody signed in.
builder.Services.AddDataProtection()
    .SetApplicationName("MatPhone")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

builder.Services.AddDbContextFactory<MpDbContext>(options => options
    // No automatic retries: a save that fails is shown to the person, not repeated behind their back.
    .UseNpgsql(config.ConnectionString())
    // Deleted users, groups and mailboxes are refused by the business layer before anything refers to them, so the warning about filtered required ends is noise.
    .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<MpDbContext>>().CreateDbContext());

builder.Services.AddSingleton<ICurrentUser, CurrentUser>();
builder.Services.AddSingleton<IconSprite>();
builder.Services.AddSingleton<SecretProtector>();
builder.Services.AddSingleton<SessionCache>();
builder.Services.AddSingleton<TwoFactorTicket>();
builder.Services.AddSingleton<ActivityLogger>();
builder.Services.AddSingleton<SetupState>();
builder.Services.AddScoped<SessionCookieEvents>();
builder.Services.AddScoped<SignInService>();
builder.Services.AddScoped<TwoFactorService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<PublicUrl>();
builder.Services.AddScoped<FileStoreService>();
builder.Services.AddScoped<MailService>();
builder.Services.AddScoped<BackupStoreFactory>();
builder.Services.AddScoped<DatabaseArchive>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddHostedService<BackupScheduler>();
builder.Services.AddHostedService<MaintenanceService>();
builder.Services.AddScoped<SystemService>();
builder.Services.AddScoped<ShellFactory>();

// The telephony engine runs in this process: one instance, started with the host, reachable for the pages as its status.
builder.Services.AddSingleton<TelephonyLogWriter>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelephonyLogWriter>());
builder.Services.AddSingleton<WebClientHub>();
builder.Services.AddSingleton<PbxEngine>();
builder.Services.AddSingleton<IPbxStatus>(sp => sp.GetRequiredService<PbxEngine>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<PbxEngine>());

builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");

builder.Services.AddAuthentication(SignInService.CookieScheme)
    .AddCookie(SignInService.CookieScheme, options =>
    {
        options.Cookie.Name = ".MatPhone";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = config.Current.Server.ForceSecureCookie ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.SlidingExpiration = false;
        options.EventsType = typeof(SessionCookieEvents);
    });

builder.Services.AddAuthorizationBuilder()
    // Everything needs a signed-in user (administrator or user) unless a page says otherwise: a new page is closed by default.
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(SignInService.CookieScheme)
        .RequireAuthenticatedUser().RequireRole(nameof(UserRole.Admin), nameof(UserRole.User)).Build())
    .AddPolicy("Admin", p => p.AddAuthenticationSchemes(SignInService.CookieScheme).RequireRole(nameof(UserRole.Admin)))
    .AddPolicy("SignedIn", p => p.AddAuthenticationSchemes(SignInService.CookieScheme).RequireAuthenticatedUser());

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", "Admin");
    options.Conventions.AuthorizeFolder("/Account", "SignedIn");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/TwoFactor");
    options.Conventions.AllowAnonymousToPage("/Account/Setup");
    options.Conventions.AllowAnonymousToPage("/Account/AccessDenied");
    options.Conventions.AllowAnonymousToPage("/Offline");
    options.Conventions.AllowAnonymousToPage("/About");
})
// A non-nullable string is not "required" by itself: the pages say what is required (the star, the message, the check).
.AddMvcOptions(options =>
{
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    // A refusal of the business layer is a 403, not an error page.
    options.Filters.Add<AccessDeniedFilter>();
})
.AddDataAnnotationsLocalization(options => options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));

// The interface speaks German and English (MatPhone.Localization); every localizer reads the one catalog of JSON texts.
// Numbers in forms and in the query string are always written the invariant way ("12.5"): only the UI culture follows the language.
builder.Services.AddSingleton<Microsoft.Extensions.Localization.IStringLocalizerFactory, CatalogLocalizerFactory>();
builder.Services.AddSingleton(typeof(Microsoft.Extensions.Localization.IStringLocalizer<>), typeof(CatalogLocalizer<>));
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(CultureInfo.InvariantCulture, Languages.UiCulture(Languages.Default));
    options.SupportedCultures = [CultureInfo.InvariantCulture];
    options.SupportedUICultures = [.. Languages.Codes.Select(Languages.UiCulture)];
    options.FallBackToParentUICultures = true;
    options.RequestCultureProviders = [new AccountLanguageProvider(), new AcceptLanguageHeaderRequestCultureProvider()];
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();
var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

await DatabaseInitializer.InitializeAsync(app.Services, config, logger);

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// A page that is not there (or not for you) is an empty 404/403 for the pipeline; the Error page says it in words.
app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-Frame-Options"] = "SAMEORIGIN";
    // The web client needs the microphone of this site; nothing else may use it.
    headers["Permissions-Policy"] = "camera=(), microphone=(self), geolocation=()";
    headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; font-src 'self'; connect-src 'self'; media-src 'self' blob:; manifest-src 'self'; " +
        "worker-src 'self'; frame-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";
    await next();
});

// The web app manifest has its own type; the service worker must never be cached by the browser, or an update would wait for days.
var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = contentTypes,
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name is "sw.js" or "manifest.webmanifest")
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        }
    },
});
app.UseAuthentication();

// After the authentication: the language of a signed-in person comes from their account.
app.UseRequestLocalization();

// While there is no administrator the only page is the one that makes the first one.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (!path.StartsWithSegments("/Account/Setup") && !path.StartsWithSegments("/language") && !path.StartsWithSegments("/health") && !path.StartsWithSegments("/css")
        && !path.StartsWithSegments("/js") && !path.StartsWithSegments("/ui") && !path.StartsWithSegments("/img") && !path.StartsWithSegments("/Offline")
        && !path.Value!.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) && !path.Value.EndsWith(".webmanifest", StringComparison.OrdinalIgnoreCase)
        && path.Value is not ("/sw.js" or "/robots.txt" or "/llms.txt")
        && !await context.RequestServices.GetRequiredService<SetupState>().HasAdminAsync(context.RequestServices))
    {
        context.Response.Redirect("/Account/Setup");
        return;
    }

    await next();
});

app.UseAuthorization();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

app.MapGet("/health", async (MpDbContext db) =>
{
    await db.Database.CanConnectAsync();
    return Results.Text("ok");
}).AllowAnonymous();

// For crawlers: nothing of an installation is public (robots.txt), and llms.txt says what the application is (llmstxt.org).
app.MapGet("/robots.txt", () => Results.Text("User-agent: *\nDisallow: /\n", "text/plain")).AllowAnonymous();
app.MapGet("/llms.txt", () => Results.Text(LlmsText.Content, "text/markdown")).AllowAnonymous();

app.MapPost("/api/theme", async (HttpContext http, IAntiforgery antiforgery, MpDbContext db, SignInService signIn, ICurrentUser user) =>
{
    await antiforgery.ValidateRequestAsync(http);
    var theme = http.Request.Form["theme"].ToString();
    if (theme is not ("system" or "light" or "dark") || user.UserId is not { } id)
    {
        return Results.BadRequest();
    }

    var entity = await db.User.Include(u => u.Totp).FirstAsync(u => u.Id == id);
    entity.Theme = theme;
    await db.SaveChangesAsync();
    await signIn.RefreshAsync(http, entity);
    return Results.NoContent();
}).RequireAuthorization("SignedIn");

// The language of the interface: kept in the account of a signed-in person and in a cookie (the sign-in page); back to where it was chosen.
app.MapPost("/language", async (HttpContext http, IAntiforgery antiforgery, MpDbContext db, SignInService signIn, ICurrentUser user) =>
{
    await antiforgery.ValidateRequestAsync(http);
    var code = Languages.Normalize(http.Request.Form["language"].ToString());
    Languages.SetCookie(http.Response, code, http.Request.IsHttps);
    if (user.UserId is { } id)
    {
        var entity = await db.User.Include(u => u.Totp).FirstAsync(u => u.Id == id);
        entity.Language = code;
        await db.SaveChangesAsync();
        await signIn.RefreshAsync(http, entity);
    }

    var back = http.Request.Form["returnUrl"].ToString();
    return Results.LocalRedirect(!string.IsNullOrEmpty(back) && back.StartsWith('/') && !back.StartsWith("//", StringComparison.Ordinal) && !back.StartsWith("/\\", StringComparison.Ordinal) ? back : "/");
}).AllowAnonymous();

// The web client: one WebSocket per open page (signalling of the calls, the state of the colleagues). Same origin only.
app.Map("/ws/phone", async (HttpContext http, WebClientHub hub, ICurrentUser user) =>
{
    if (!http.WebSockets.IsWebSocketRequest || user.UserId is not { } id)
    {
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var origin = http.Request.Headers.Origin.ToString();
    if (origin.Length > 0 && (!Uri.TryCreate(origin, UriKind.Absolute, out var o) || !string.Equals(o.Authority, http.Request.Host.Value, StringComparison.OrdinalIgnoreCase)))
    {
        http.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    using var socket = await http.WebSockets.AcceptWebSocketAsync();
    await hub.RunAsync(socket, id, user.Name ?? "", http.RequestAborted);
}).RequireAuthorization("SignedIn");

// The recording of a message of a mailbox (the own ones and those of the own groups): played in the page, or saved with ?download=1. Listening marks it as heard.
app.MapGet("/voicemail/{id:long}.wav", async (long id, HttpContext http, MpDbContext db, ICurrentUser user, FileStoreService files) =>
{
    var audio = await Mailboxes.AudioAsync(db, Actor.From(user), id, files, http.RequestAborted);
    if (audio is not { } a)
    {
        return Results.NotFound();
    }

    http.Response.Headers.CacheControl = "private, no-store";
    return Results.File(a.Wav, "audio/wav", http.Request.Query.ContainsKey("download") ? a.FileName : null, enableRangeProcessing: true);
}).RequireAuthorization("SignedIn");

// The greeting of a mailbox, to listen to it on the page of the mailbox.
app.MapGet("/greeting/{id:long}.wav", async (long id, HttpContext http, MpDbContext db, ICurrentUser user, FileStoreService files) =>
{
    var wav = await Mailboxes.GreetingAsync(db, Actor.From(user), id, files, http.RequestAborted);
    http.Response.Headers.CacheControl = "private, no-store";
    return wav is null ? Results.NotFound() : Results.File(wav, "audio/wav", enableRangeProcessing: true);
}).RequireAuthorization("SignedIn");

// A greeting recorded in the browser (the page sends a WAV it made from the microphone).
app.MapPost("/greeting/{id:long}", async (long id, HttpContext http, IAntiforgery antiforgery, MpDbContext db, ICurrentUser user, FileStoreService files) =>
{
    await antiforgery.ValidateRequestAsync(http);
    var file = http.Request.Form.Files.FirstOrDefault();
    if (file is null || file.Length is 0 or > 10 * 1024 * 1024)
    {
        return Results.BadRequest(new { error = Text.T("Mailboxes.Greeting.NotWav") });
    }

    using var stream = new MemoryStream();
    await file.CopyToAsync(stream, http.RequestAborted);
    var problem = await Mailboxes.SetGreetingAsync(db, Actor.From(user), id, stream.ToArray(), files, http.RequestAborted);
    return problem is null ? Results.NoContent() : Results.BadRequest(new { error = problem });
}).RequireAuthorization("SignedIn");

// The badge of the shell and the page title of the web client: new messages.
app.MapGet("/api/voicemail/count", async (MpDbContext db, ICurrentUser user)
    => Results.Ok(new { unread = await Mailboxes.NewCountAsync(db, Actor.From(user)) })).RequireAuthorization("SignedIn");

app.MapRazorPages();

await app.RunAsync();
return 0;

/// <summary>Whether the first administrator exists: the question is asked on every request until the answer is yes, then never again.</summary>
public sealed class SetupState
{
    private volatile bool _done;

    public async Task<bool> HasAdminAsync(IServiceProvider services)
    {
        if (_done)
        {
            return true;
        }

        var db = services.GetRequiredService<MpDbContext>();
        _done = await db.User.AnyAsync(u => u.Role == UserRole.Admin);
        return _done;
    }

    public void MarkDone() => _done = true;
}

/// <summary>The command <c>dotnet MatPhone.dll --healthcheck</c>: asks the running site; exit code 0 means healthy.</summary>
public static class HealthCheck
{
    public static async Task<int> RunAsync()
    {
        var port = Environment.GetEnvironmentVariable("MATPHONE_PORT") ?? "6050";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync($"http://localhost:{port}/health");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }
}

/// <summary>Makes the entry point visible to the tests (<c>WebApplicationFactory&lt;Program&gt;</c>).</summary>
public partial class Program;
