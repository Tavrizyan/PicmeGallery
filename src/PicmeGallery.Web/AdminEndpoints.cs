using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;

namespace PicmeGallery.Web;

public sealed class AdminCredentials(IConfiguration configuration)
{
    public bool Configured => !string.IsNullOrWhiteSpace(configuration["Admin:Username"]) && !string.IsNullOrEmpty(configuration["Admin:Password"]);
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configuration["Admin:Username"] + "\0" + configuration["Admin:Password"])));
    public bool Matches(string? username, string? password)
    {
        if (!Configured || username is null || password is null || username.Length > 128 || password.Length > 1024) return false;
        bool user = CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(username)), SHA256.HashData(Encoding.UTF8.GetBytes(configuration["Admin:Username"]!)));
        bool pass = CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(password)), SHA256.HashData(Encoding.UTF8.GetBytes(configuration["Admin:Password"]!)));
        return user & pass;
    }
}

public static class AdminEndpoints
{
    public const string Scheme = "GalleryAdmin";
    public static IServiceCollection AddGalleryAdministration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<AdminCredentials>();
        var protection = services.AddDataProtection().SetApplicationName("PicmeGallery");
        if (configuration["Admin:DataProtectionKeysPath"] is { Length: > 0 } keysPath)
            protection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
        {
            options.Cookie.Name = "__Host-Picme.Admin"; options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = CookieSecurePolicy.Always; options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromHours(8); options.SlidingExpiration = false;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
            options.Events.OnValidatePrincipal = async context =>
            {
                var credentials = context.HttpContext.RequestServices.GetRequiredService<AdminCredentials>();
                if (!credentials.Configured || context.Principal?.FindFirstValue("credentials") != credentials.Fingerprint)
                {
                    context.RejectPrincipal(); await context.HttpContext.SignOutAsync(Scheme);
                }
            };
        });
        services.AddAuthorization();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-Token"; options.Cookie.Name = "__Host-Picme.Csrf";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always; options.Cookie.HttpOnly = true; options.Cookie.SameSite = SameSiteMode.Strict;
        });
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("admin-login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddConcurrencyLimiter("event-zip", limiter => { limiter.PermitLimit = 2; limiter.QueueLimit = 0; });
            options.OnRejected = async (context, cancellation) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                await context.HttpContext.Response.WriteAsJsonAsync(new { message = "Too many requests. Please wait a minute and try again." }, cancellation);
            };
        });
        return services;
    }

    public static void MapGalleryAdministration(this WebApplication app)
    {
        app.MapGet("/admin/login", (IWebHostEnvironment env) => Results.File(Path.Combine(env.WebRootPath, "admin.html"), "text/html"));
        app.MapGet("/admin", (HttpContext context, IWebHostEnvironment env) => context.User.Identity?.IsAuthenticated == true
            ? Results.File(Path.Combine(env.WebRootPath, "admin.html"), "text/html") : Results.Redirect("/admin/login"));
        app.MapGet("/api/admin/session", (HttpContext context, IAntiforgery antiforgery) => Results.Ok(new
        {
            authenticated = context.User.Identity?.IsAuthenticated == true,
            username = context.User.Identity?.Name, csrfToken = antiforgery.GetAndStoreTokens(context).RequestToken
        }));
        var api = app.MapGroup("/api/admin");
        api.AddEndpointFilter(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
            {
                try { await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext); }
                catch (AntiforgeryValidationException) { return Results.BadRequest(new { message = "Your session changed. Refresh the page and try again." }); }
            }
            try { return await next(context); }
            catch (EventConflictException) { return Results.Conflict(new { message = "This event already exists or was changed in another tab. Refresh the event list and try again." }); }
        });
        api.MapPost("/login", async (LoginInput input, HttpContext context, AdminCredentials credentials, IConfiguration config) =>
        {
            if (!credentials.Configured) return Results.Json(new { message = "Admin sign-in is not configured." }, statusCode: 503);
            if (!credentials.Matches(input.Username, input.Password)) return Results.Json(new { message = "Incorrect username or password." }, statusCode: 401);
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, config["Admin:Username"]!), new Claim("credentials", credentials.Fingerprint)], Scheme);
            await context.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = false, AllowRefresh = false });
            return Results.Ok(new { success = true });
        }).RequireRateLimiting("admin-login");
        api.MapPost("/logout", async (HttpContext context) => { await context.SignOutAsync(Scheme); return Results.Ok(new { success = true }); }).RequireAuthorization();
        api.MapGet("/events", async (string? cursor, EventCatalog catalog, IDataProtectionProvider protection, CancellationToken cancellation) =>
        {
            if (!Cursors.TryRead(protection, "admin", cursor, out string? continuation)) return Results.BadRequest(new { message = "Invalid page. Refresh the event list." });
            AdminEventCursor? after = null;
            if (continuation is not null)
            {
                try { after = JsonSerializer.Deserialize<AdminEventCursor>(continuation); }
                catch (JsonException) { return Results.BadRequest(new { message = "Invalid page. Refresh the event list." }); }
                if (after is null || !GalleryContract.ValidEventId(after.EventId ?? "")) return Results.BadRequest(new { message = "Invalid page. Refresh the event list." });
            }
            var all = await catalog.ListAsync(cancellation);
            var remaining = all.Where(item => after is null || item.Record.CreatedUtc < after.CreatedUtc ||
                (item.Record.CreatedUtc == after.CreatedUtc && string.CompareOrdinal(item.Record.EventId, after.EventId) > 0)).Take(31).ToArray();
            var page = remaining.Take(30).ToArray();
            string? next = remaining.Length > 30 ? JsonSerializer.Serialize(new AdminEventCursor(page[^1].Record.CreatedUtc, page[^1].Record.EventId)) : null;
            return Results.Ok(new { events = page.Select(item => Describe(item, protection)), nextCursor = Cursors.Write(protection, "admin", next) });
        }).RequireAuthorization();
        api.MapPost("/events", async (CreateEventInput input, IEventStore store, IDataProtectionProvider protection, CancellationToken cancellation) =>
        {
            string eventId = input.EventId?.Trim() ?? "", name = input.Name?.Trim() ?? "";
            if (!GalleryContract.ValidEventId(eventId) || name.Length is < 1 or > 120) return Results.BadRequest(new { message = "Enter an event name (up to 120 characters) and the exact booth Event ID: lowercase letters, numbers, hyphens or underscores, up to 64 characters." });
            string token = EventAccess.NewToken();
            var record = new EventRecord(eventId, name, DateTimeOffset.UtcNow, GalleryContract.TokenHash(token), protection.CreateProtector("EventOwnerTokens").Protect(token));
            await store.SaveAsync(record, null, cancellation);
            return Results.Ok(new { ownerPath = "/e/" + token });
        }).RequireAuthorization();
        api.MapPost("/events/{eventId}/{action}", async (string eventId, string action, EventVersionInput input, IEventStore store, IDataProtectionProvider protection, CancellationToken cancellation) =>
        {
            if (!GalleryContract.ValidEventId(eventId) || action is not ("replace" or "revoke")) return Results.NotFound();
            var stored = await store.GetAsync(eventId, cancellation);
            if (stored is null) return Results.NotFound();
            if (input.Version != stored.Version) throw new EventConflictException();
            string? token = action == "replace" ? EventAccess.NewToken() : null;
            var updated = stored.Record with
            {
                TokenHash = token is null ? null : GalleryContract.TokenHash(token),
                ProtectedToken = token is null ? null : protection.CreateProtector("EventOwnerTokens").Protect(token)
            };
            await store.SaveAsync(updated, stored.Version, cancellation);
            return Results.Ok(new { ownerPath = token is null ? null : "/e/" + token });
        }).RequireAuthorization();
    }
    private static object Describe(StoredEvent stored, IDataProtectionProvider protection)
    {
        string? ownerPath = null;
        if (stored.Record.ProtectedToken is { } encrypted)
        {
            try { ownerPath = "/e/" + protection.CreateProtector("EventOwnerTokens").Unprotect(encrypted); }
            catch (CryptographicException) { /* Missing keys: an administrator can replace the owner link. */ }
        }
        return new { stored.Record.EventId, stored.Record.Name, stored.Record.CreatedUtc, active = stored.Record.TokenHash is not null, ownerPath, stored.Version };
    }
    public sealed record LoginInput(string? Username, string? Password);
    public sealed record CreateEventInput(string? EventId, string? Name);
    public sealed record EventVersionInput(string? Version);
}

internal static class Cursors
{
    public static string? Write(IDataProtectionProvider provider, string scope, string? continuation) => continuation is null ? null
        : provider.CreateProtector("GalleryPaging", scope).Protect(continuation);
    public static bool TryRead(IDataProtectionProvider provider, string scope, string? cursor, out string? continuation)
    {
        continuation = null;
        if (cursor is null) return true;
        if (cursor.Length > 16384) return false;
        try { continuation = provider.CreateProtector("GalleryPaging", scope).Unprotect(cursor); return true; }
        catch (CryptographicException) { return false; }
    }
}
