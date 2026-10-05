using System.Text.Json.Serialization;

namespace Vograph.Core.Models;

public class Homework
{
    public long Id { get; set; }
    public string SubjectRawNormalized { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int TargetNthOccurrence { get; set; } // 1..10
    public DateTime? DueDateComputed { get; set; }
    public string Status { get; set; } = "pending"; // pending|approaching|burning|done|overdue
    public DateTime? DoneAt { get; set; }
    [JsonIgnore] public Guid? EntityId { get; set; }
    [JsonIgnore] public long Revision { get; set; }
    [JsonIgnore] public bool Tombstone { get; set; }
    [JsonIgnore] public DateTimeOffset? CreatedAtUtc { get; set; }
    [JsonIgnore] public DateOnly? LegacyCreatedLocalDate { get; set; }
}
