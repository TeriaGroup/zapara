namespace Vograph.Core.Models;

public class Group
{
    public string Id { get; set; } = ""; // IdGroup
    public string Name { get; set; } = ""; // Number like А863С
    public string Url { get; set; } = ""; // source xml url
    public DateTime? LastFetchedAt { get; set; }
    public string? RawXml { get; set; }
}
