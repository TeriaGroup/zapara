using System.Text.Json.Serialization;

namespace Zapara.Contracts.Communities;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BallotDraftRequest
{
    [JsonConstructor]
    public BallotDraftRequest(string question, IReadOnlyList<string> options, int days)
    {
        Question = CommunityValidation.Question(question);
        if (options is null || options.Count is < 2 or > 6) throw CommunityValidation.Invalid();
        Options = CommunityValidation.Options(options);
        if (days is < 1 or > 14) throw CommunityValidation.Invalid();
        Days = days;
    }

    [JsonRequired, JsonInclude] public string Question { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<string> Options { get; private init; }
    [JsonRequired, JsonInclude] public int Days { get; private init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BallotChangeRequest
{
    [JsonConstructor]
    public BallotChangeRequest(string kind, int days, Guid roleId, Guid userId, string name, string power, bool enabled)
    {
        if (kind is not ("power" or "grant" or "revoke_grant" or "create_role" or "rename_role" or "delete_role" or "remove_member"))
            throw CommunityValidation.Invalid();
        if (days is < 1 or > 14) throw CommunityValidation.Invalid();
        if (name is null || name.Length > 80 || power is null || power.Length > 32) throw CommunityValidation.Invalid();
        Kind = kind;
        Days = days;
        RoleId = roleId;
        UserId = userId;
        Name = name;
        Power = power;
        Enabled = enabled;
    }

    [JsonRequired, JsonInclude] public string Kind { get; private init; }
    [JsonRequired, JsonInclude] public int Days { get; private init; }
    [JsonRequired, JsonInclude] public Guid RoleId { get; private init; }
    [JsonRequired, JsonInclude] public Guid UserId { get; private init; }
    [JsonRequired, JsonInclude] public string Name { get; private init; }
    [JsonRequired, JsonInclude] public string Power { get; private init; }
    [JsonRequired, JsonInclude] public bool Enabled { get; private init; }
}

public sealed record BallotOptionResponse
{
    [JsonConstructor]
    public BallotOptionResponse(Guid optionId, string label, int votes, bool chosen)
    {
        OptionId = optionId;
        Label = label;
        Votes = votes < 0 ? 0 : votes;
        Chosen = chosen;
    }

    [JsonRequired, JsonInclude] public Guid OptionId { get; private init; }
    [JsonRequired, JsonInclude] public string Label { get; private init; }
    [JsonRequired, JsonInclude] public int Votes { get; private init; }
    [JsonRequired, JsonInclude] public bool Chosen { get; private init; }
}

public sealed record BallotResponse
{
    [JsonConstructor]
    public BallotResponse(Guid ballotId, string question, string origin, string status, DateTimeOffset deadlineAt, int supporters, int supportersNeeded, bool supported, IReadOnlyList<BallotOptionResponse> options, string effect, string outcome)
    {
        BallotId = ballotId;
        Question = question ?? "";
        Origin = origin ?? "";
        Status = status ?? "";
        DeadlineAt = deadlineAt;
        Supporters = supporters < 0 ? 0 : supporters;
        SupportersNeeded = supportersNeeded < 1 ? 1 : supportersNeeded;
        Supported = supported;
        Options = options ?? Array.Empty<BallotOptionResponse>();
        Effect = effect ?? "";
        Outcome = outcome ?? "";
    }

    [JsonRequired, JsonInclude] public Guid BallotId { get; private init; }
    [JsonRequired, JsonInclude] public string Question { get; private init; }
    [JsonRequired, JsonInclude] public string Origin { get; private init; }
    [JsonRequired, JsonInclude] public string Status { get; private init; }
    [JsonRequired, JsonInclude] public DateTimeOffset DeadlineAt { get; private init; }
    [JsonRequired, JsonInclude] public int Supporters { get; private init; }
    [JsonRequired, JsonInclude] public int SupportersNeeded { get; private init; }
    [JsonRequired, JsonInclude] public bool Supported { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<BallotOptionResponse> Options { get; private init; }
    [JsonRequired, JsonInclude] public string Effect { get; private init; }
    [JsonRequired, JsonInclude] public string Outcome { get; private init; }
}

public sealed record BallotBoardResponse
{
    [JsonConstructor]
    public BallotBoardResponse(bool headman, bool canOpen, bool canClose, int members, int supportersNeeded, IReadOnlyList<BallotResponse> ballots)
    {
        Headman = headman;
        CanOpen = canOpen;
        CanClose = canClose;
        Members = members < 0 ? 0 : members;
        SupportersNeeded = supportersNeeded < 1 ? 1 : supportersNeeded;
        Ballots = ballots ?? Array.Empty<BallotResponse>();
    }

    [JsonRequired, JsonInclude] public bool Headman { get; private init; }
    [JsonRequired, JsonInclude] public bool CanOpen { get; private init; }
    [JsonRequired, JsonInclude] public bool CanClose { get; private init; }
    [JsonRequired, JsonInclude] public int Members { get; private init; }
    [JsonRequired, JsonInclude] public int SupportersNeeded { get; private init; }
    [JsonRequired, JsonInclude] public IReadOnlyList<BallotResponse> Ballots { get; private init; }
}
