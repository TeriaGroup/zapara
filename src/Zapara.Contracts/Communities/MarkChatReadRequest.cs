using System.Text.Json.Serialization;

namespace Zapara.Contracts.Communities;

public sealed record MarkChatReadRequest
{
    [JsonConstructor]
    public MarkChatReadRequest(Guid throughMessageId) => ThroughMessageId = CommunityValidation.Id(throughMessageId);

    [JsonRequired, JsonInclude] public Guid ThroughMessageId { get; private init; }
}
