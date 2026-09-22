using System.Text.Json.Serialization;

namespace Vograph.Core.Models;

public class Settings
{
    public string? MyGroupId { get; set; }
    public bool ParityInvert { get; set; } = false;
    public string? NotifyTime1 { get; set; } // HH:mm
    public string? NotifyTime2 { get; set; }
    public int IntersectionStrictness { get; set; } = 25; // 0..100 — default 25 = "в вузе" visible, 0=any, 100=same room (5 gradations: 25 в вузе, 50 корпус, 75 этаж, 100 аудитория)
    public string Language { get; set; } = "ru"; // 'ru' | 'en', default ru per §2
    public DateTime? LastSyncAt { get; set; }
    public string? LastFetchedAt { get; set; }
    public string? LastAutoCheckAt { get; set; } // ISO, for auto-refresh 24h timer
    public int WeekCount { get; set; } = 2;
    public string? PeriodTitle { get; set; }
    public string? PeriodStart { get; set; } // YYYY-MM-DD
    public int MapPanelWidth { get; set; } = 300;
    public bool AlwaysShowAllTrafficLights { get; set; } = false;
    public bool AutoUpdate { get; set; } = true;
    [JsonIgnore] public Guid? EntityId { get; set; }
    [JsonIgnore] public long Revision { get; set; }
}
