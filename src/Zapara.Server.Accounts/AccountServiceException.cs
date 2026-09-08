namespace Zapara.Server.Accounts;

public enum AccountFailure { InvalidCredentials, InvalidSession, UsernameUnavailable, SessionNotFound, InvalidRequest, RateLimited, DbUnavailable, ExportNotFound }

public sealed class AccountServiceException : Exception
{
    public AccountServiceException(AccountFailure failure) : base("Операция аккаунта отклонена.") => Failure = failure;
    public AccountFailure Failure { get; }
    public string Code => Failure switch
    {
        AccountFailure.InvalidCredentials => "invalid_credentials",
        AccountFailure.InvalidSession => "invalid_session",
        AccountFailure.UsernameUnavailable => "username_unavailable",
        AccountFailure.SessionNotFound => "session_not_found",
        AccountFailure.InvalidRequest => "invalid_request",
        AccountFailure.RateLimited => "rate_limited",
        AccountFailure.DbUnavailable => "db_unavailable",
        AccountFailure.ExportNotFound => "export_not_found",
        _ => "internal_error"
    };
}
