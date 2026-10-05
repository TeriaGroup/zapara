namespace Zapara.Server.Admin;

public sealed class AdminException : Exception
{
    public AdminException(int status, string code) : base("Операция админки отклонена.")
        => (Status, Code) = (status, code);
    public int Status { get; }
    public string Code { get; }
    public static AdminException Invalid() => new(400, "invalid_request");
    public static AdminException Unauthorized() => new(401, "invalid_credentials");
    public static AdminException Reauth() => new(401, "reauth_required");
    public static AdminException Forbidden() => new(403, "forbidden");
    public static AdminException NotFound() => new(404, "not_found");
    public static AdminException Conflict(string code) => new(409, code);
    public static AdminException Unavailable() => new(503, "db_unavailable");
}
