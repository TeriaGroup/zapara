using System.Text.Json.Serialization;

namespace Vograph.Core.Models;

public class Settings
{
    private static readonly int[] PrecisionLevels = { 50, 75, 100 };
    private int _intersectionStrictness = 50;
    public string? MyGroupId { get; set; }
    public bool ParityInvert { get; set; } = false;
    public string? NotifyTime1 { get; set; } // HH:mm
    public string? NotifyTime2 { get; set; }
    // Reading a legacy database or import also uses the remaining supported levels.
    public int IntersectionStrictness
    {
        get => _intersectionStrictness;
        set
        {
            var bounded = Math.Clamp(value, 50, 100);
            _intersectionStrictness = PrecisionLevels.MinBy(level => Math.Abs(level - bounded));
        }
    }
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
