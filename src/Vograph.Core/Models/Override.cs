using System.Text.Json.Serialization;

namespace Vograph.Core.Models;

public class Override
{
    public long Id { get; set; }
    public string SubjectRawNormalized { get; set; } = "";
    public string Scope { get; set; } = ""; // "global" or "weekday:3"
    public string DisplayName { get; set; } = "";
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    [JsonIgnore] public Guid? EntityId { get; set; }
    [JsonIgnore] public long Revision { get; set; }
    [JsonIgnore] public bool Tombstone { get; set; }
    [JsonIgnore] public DateTimeOffset? CreatedAtUtc { get; set; }
}
