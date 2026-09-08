namespace Zapara.Server.Communities;

public sealed class CommunityServiceException : Exception
{
    public CommunityServiceException(int status, string code) : base("Операция сообщества отклонена.")
        => (Status, Code) = (status, code);
    public int Status { get; }
    public string Code { get; }
    internal static CommunityServiceException InvalidRequest() => new(400, "invalid_request");
    internal static CommunityServiceException Forbidden() => new(403, "forbidden");
    internal static CommunityServiceException NotFound() => new(404, "not_found");
    internal static CommunityServiceException Conflict(string code) => new(409, code);
}
