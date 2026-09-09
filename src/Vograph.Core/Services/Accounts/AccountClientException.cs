namespace Vograph.Core.Services.Accounts;

public enum AccountClientFailure
{
    InvalidRequest, InvalidCredentials, InvalidSession, UsernameUnavailable, SessionNotFound,
    RateLimited, DbUnavailable, RegistrationUnavailable, NotConfigured, ServerUnavailable,
    InvalidPayload, BodyTooLarge, Transport, Timeout, VaultUnavailable, ReauthenticationRequired,
    SessionChanged, LockTimeout, InternalError, InvalidExternalProof, ProviderUnavailable
}

public sealed class AccountClientException(AccountClientFailure failure, int? status = null,
    TimeSpan? retryAfter = null) : Exception("Операция аккаунта не выполнена.")
{
    public AccountClientFailure Failure { get; } = failure;
    public int? Status { get; } = status;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public string Code => Failure switch
    {
        AccountClientFailure.InvalidRequest => "invalid_request",
        AccountClientFailure.InvalidCredentials => "invalid_credentials",
        AccountClientFailure.InvalidSession => "invalid_session",
        AccountClientFailure.UsernameUnavailable => "username_unavailable",
        AccountClientFailure.SessionNotFound => "session_not_found",
        AccountClientFailure.RateLimited => "rate_limited",
        AccountClientFailure.DbUnavailable => "db_unavailable",
        AccountClientFailure.RegistrationUnavailable => "registration_unavailable",
        AccountClientFailure.NotConfigured => "not_configured",
        AccountClientFailure.InternalError => "internal_error",
        AccountClientFailure.InvalidExternalProof => "invalid_external_proof",
        AccountClientFailure.ProviderUnavailable => "provider_unavailable",
        _ => "account_operation_failed"
    };
}
