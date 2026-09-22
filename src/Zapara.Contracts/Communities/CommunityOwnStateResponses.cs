using System.Text.Json.Serialization;

namespace Zapara.Contracts.Communities;

public sealed record OwnJoinRequestResponse([property: JsonRequired] JoinRequestResponse? Request);
public sealed record OwnVoteResponse([property: JsonRequired] VoteResponse? Vote);
