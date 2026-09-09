namespace Vograph.Core.Services.Communities;

public enum CommunityClientFailure
{
    InvalidRequest, InvalidSession, Forbidden, NotFound,
    RevisionConflict, AlreadyVoted, AlreadyMember, AlreadyRequested, PollClosed,
    PayloadTooLarge, RateLimited, DbUnavailable, InternalError, ServerUnavailable,
    InvalidPayload, BodyTooLarge, Transport, Timeout
}

public sealed class CommunityClientException(CommunityClientFailure failure, int? status = null,
    TimeSpan? retryAfter = null) : Exception("Операция сообщества не выполнена.")
{
    public CommunityClientFailure Failure { get; } = failure;
    public int? Status { get; } = status;
    public TimeSpan? RetryAfter { get; } = retryAfter;
    public string Code => Failure switch
    {
        CommunityClientFailure.InvalidRequest => "invalid_request",
        CommunityClientFailure.InvalidSession => "invalid_session",
        CommunityClientFailure.Forbidden => "forbidden",
        CommunityClientFailure.NotFound => "not_found",
        CommunityClientFailure.RevisionConflict => "revision_conflict",
        CommunityClientFailure.AlreadyVoted => "already_voted",
        CommunityClientFailure.AlreadyMember => "already_member",
        CommunityClientFailure.AlreadyRequested => "already_requested",
        CommunityClientFailure.PollClosed => "poll_closed",
        CommunityClientFailure.PayloadTooLarge => "payload_too_large",
        CommunityClientFailure.RateLimited => "rate_limited",
        CommunityClientFailure.DbUnavailable => "db_unavailable",
        CommunityClientFailure.InternalError => "internal_error",
        _ => "community_operation_failed"
    };
}
