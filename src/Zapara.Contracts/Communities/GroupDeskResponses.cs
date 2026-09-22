using System.Text.Json.Serialization;

namespace Zapara.Contracts.Communities;

public sealed record GroupRoleNameRequest
{
    [JsonConstructor]
    public GroupRoleNameRequest(string name) => Name = name;
    [JsonRequired, JsonInclude] public string Name { get; private init; }
}

public sealed record GroupGrantRequest
{
    [JsonConstructor]
    public GroupGrantRequest(Guid userId) => UserId = userId;
    [JsonRequired, JsonInclude] public Guid UserId { get; private init; }
}

public sealed record GroupRoleResponse
{
    [JsonConstructor]
    public GroupRoleResponse(Guid roleId, string name)
    {
        RoleId = roleId;
        Name = name;
    }
    [JsonRequired, JsonInclude] public Guid RoleId { get; private init; }
    [JsonRequired, JsonInclude] public string Name { get; private init; }
}

public sealed record GroupGrantResponse
{
    [JsonConstructor]
    public GroupGrantResponse(Guid roleId, Guid userId)
    {
        RoleId = roleId;
        UserId = userId;
    }
    [JsonRequired, JsonInclude] public Guid RoleId { get; private init; }
    [JsonRequired, JsonInclude] public Guid UserId { get; private init; }
}

public sealed record GroupApplicantResponse
{
    [JsonConstructor]
    public GroupApplicantResponse(Guid requestId, Guid userId, string username, string? displayName)
    {
        RequestId = requestId;
        UserId = userId;
        Username = username;
        DisplayName = displayName;
    }
    [JsonRequired, JsonInclude] public Guid RequestId { get; private init; }
    [JsonRequired, JsonInclude] public Guid UserId { get; private init; }
    [JsonRequired, JsonInclude] public string Username { get; private init; }
    [JsonRequired, JsonInclude] public string? DisplayName { get; private init; }
}

public sealed record GroupPowerResponse
{
    [JsonConstructor]
    public GroupPowerResponse(Guid roleId, string power)
    {
        RoleId = roleId;
        Power = power ?? "";
    }
    [JsonRequired, JsonInclude] public Guid RoleId { get; private init; }
    [JsonRequired, JsonInclude] public string Power { get; private init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GroupPowerRequest
{
    [JsonConstructor]
    public GroupPowerRequest(string power, bool enabled)
    {
        if (power is null || power.Length is < 1 or > 32) throw CommunityValidation.Invalid();
        Power = power;
        Enabled = enabled;
    }
    [JsonRequired, JsonInclude] public string Power { get; private init; }
    [JsonRequired, JsonInclude] public bool Enabled { get; private init; }
}

public sealed record GroupDeskResponse
{
    [JsonConstructor]
    public GroupDeskResponse(bool headman, IReadOnlyList<GroupRoleResponse> roles, IReadOnlyList<GroupGrantResponse> grants, IReadOnlyList<GroupApplicantResponse> applicants, IReadOnlyList<GroupPowerResponse> powers, IReadOnlyList<string> mine)
    {
        Headman = headman;
        Roles = roles ?? throw new ArgumentException();
        Grants = grants ?? throw new ArgumentException();
        Applicants = applicants ?? throw new ArgumentException();
        Powers = powers ?? throw new ArgumentException();
        Mine = mine ?? throw new ArgumentException();
    }
    [JsonRequired, JsonInclude] public bool Headman { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<GroupRoleResponse> Roles { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<GroupGrantResponse> Grants { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<GroupApplicantResponse> Applicants { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<GroupPowerResponse> Powers { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<string> Mine { get; private init; }
}
