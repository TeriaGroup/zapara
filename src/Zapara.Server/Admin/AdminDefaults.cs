namespace Zapara.Server.Admin;

public static class AdminDefaults
{
    public const string Scheme = "AdminCookie";
    public const string CookieName = ".Zapara.Admin";
    public const string Policy = "AdminUser";
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    public static readonly TimeSpan ReauthLifetime = TimeSpan.FromMinutes(5);
}
