using System.Text.Json.Serialization;

namespace Zapara.Contracts.Communities;

/// <summary>Active members selected directly or through any listed group role.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HomeworkAudience
{
    public static HomeworkAudience All { get; } = new("all", [], []);

    [JsonConstructor]
    public HomeworkAudience(string kind, IReadOnlyList<Guid> roleIds, IReadOnlyList<Guid> userIds)
    {
        if (kind is not ("all" or "selected") || roleIds is null || userIds is null || roleIds.Count > 100 || userIds.Count > 500)
            throw CommunityValidation.Invalid();
        Kind = kind;
        RoleIds = Array.AsReadOnly(roleIds.Select(CommunityValidation.Id).Distinct().Order().ToArray());
        UserIds = Array.AsReadOnly(userIds.Select(CommunityValidation.Id).Distinct().Order().ToArray());
        if ((kind == "all") != (RoleIds.Count + UserIds.Count == 0)) throw CommunityValidation.Invalid();
    }
    [JsonRequired, JsonInclude] public string Kind { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<Guid> RoleIds { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<Guid> UserIds { get; private init; }
}
