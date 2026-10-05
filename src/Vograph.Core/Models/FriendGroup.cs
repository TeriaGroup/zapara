using System.Text.Json.Serialization;

namespace Vograph.Core.Models;

public class FriendGroup
{
    public long Id { get; set; }
    public string GroupName { get; set; } = "";
    public string ColorHex { get; set; } = "#FF6CA5E0";
    public bool Enabled { get; set; } = true;
    public string MemberNames { get; set; } = "";
    [JsonIgnore] public Guid? EntityId { get; set; }
    [JsonIgnore] public long Revision { get; set; }
    [JsonIgnore] public bool Tombstone { get; set; }
}
