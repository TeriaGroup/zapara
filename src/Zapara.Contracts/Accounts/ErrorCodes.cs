namespace Zapara.Contracts.Accounts;

public static class ErrorCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string UsernameUnavailable = "username_unavailable";
    public const string InvalidCredentials = "invalid_credentials";
    public const string InvalidSession = "invalid_session";
    public const string SessionNotFound = "session_not_found";
    public const string RateLimited = "rate_limited";
    public const string DbUnavailable = "db_unavailable";
    public const string InternalError = "internal_error";
    public const string RecoveryUnavailable = "recovery_unavailable";
    public const string ExportNotFound = "export_not_found";
}
