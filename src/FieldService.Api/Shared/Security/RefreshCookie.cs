namespace FieldService.Api.Shared.Security;

public static class RefreshCookie
{
    public const string Name = "fs_refresh";

    public const string Path = "/api/auth";

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    public static void Set(HttpContext context, string token, DateTimeOffset now, DateTimeOffset idleExpiresAt)
    {
        var seconds = Math.Max(0L, (long)Math.Floor((idleExpiresAt - now).TotalSeconds));
        context.Response.Cookies.Append(Name, token, Options(context, TimeSpan.FromSeconds(seconds), expires: null));
    }

    public static void Clear(HttpContext context)
    {
        context.Response.Cookies.Append(Name, string.Empty, Options(context, TimeSpan.Zero, DateTimeOffset.UnixEpoch));
    }

    private static CookieOptions Options(HttpContext context, TimeSpan maxAge, DateTimeOffset? expires) => new()
    {
        Path = Path,
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = IsSecure(context),
        MaxAge = maxAge,
        Expires = expires,
    };

    private static bool IsSecure(HttpContext context) =>
        !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() || context.Request.IsHttps;
}
