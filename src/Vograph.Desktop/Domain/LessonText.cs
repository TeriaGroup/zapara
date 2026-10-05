using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Domain;

/// <summary>Display forms of a lesson's raw strings. Display only — Core is always called with the full SubjectRaw.</summary>
public static class LessonText
{
    /// <summary>"пр ОСН РОС ГОС" → "ОСН РОС ГОС" when the name starts with the lesson's own type token.</summary>
    public static string StripType(string name, string typeRaw)
    {
        var t = typeRaw.Trim();
        return t.Length > 0 && name.Length > t.Length + 1 && name.StartsWith(t + " ", StringComparison.OrdinalIgnoreCase)
            ? name[(t.Length + 1)..].Trim()
            : name;
    }

    /// <summary>«526*; » → «526», «ВЦ 280; » → «ВЦ 280»: the classroom the way a person writes it.</summary>
    public static string CleanRoom(string classroomRaw) => classroomRaw.Trim().TrimEnd(';').Replace("*", "").Trim();

    /// <summary>Room chip text, building tag and the remote flag for a lesson card / week row.</summary>
    public static (string Room, string? Tag, bool Remote) RoomParts(Lesson l, MapInfo? map, Loc loc)
    {
        if (map is null) return (string.IsNullOrWhiteSpace(l.RoomRaw) ? "—" : CleanRoom(l.RoomRaw), null, false);
        if (map.IsRemote) return (loc.T("remote"), null, true);
        if (map.Building == "ВЦ") return ($"ВЦ {map.RoomRaw}", "ГК", false);
        return (map.RoomRaw, map.Building, false);
    }
}
