using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Domain;

/// <summary>Display forms of a lesson's raw strings. Display only — Core is always called with the full SubjectRaw.</summary>
public static class LessonText
{
    /// <summary>«пр ОСН.РОС.ГОС» → «Основы российской государственности»: без префикса типа, читаемым регистром (#12, ScheduleText).
    /// Имена, заданные студентом (не капсом), не меняются.</summary>
    public static string StripType(string name, string typeRaw) => ScheduleText.SubjectShort(name, typeRaw);

    /// <summary>Полное имя предмета для подсказки/подробностей.</summary>
    public static string FullName(string name, string typeRaw) => ScheduleText.Subject(name, typeRaw).Full;

    /// <summary>«526*; » → «526», «268*(фесто);» → «268 (Фесто)»: аудитория так, как её пишет человек.</summary>
    public static string CleanRoom(string classroomRaw) => ScheduleText.Room(classroomRaw);

    /// <summary>«Кондратьев Сергей А.» → «Кондратьев С. А.».</summary>
    public static string Teacher(string? teacherRaw) => ScheduleText.Teacher(teacherRaw);

    /// <summary>Room chip text, building tag and the remote flag for a lesson card / week row.</summary>
    public static (string Room, string? Tag, bool Remote) RoomParts(Lesson l, MapInfo? map, Loc loc)
    {
        if (map is null) return (string.IsNullOrWhiteSpace(l.RoomRaw) ? "—" : CleanRoom(l.RoomRaw), null, false);
        // map.RoomRaw is already the bare number from the map catalog.
        if (map.IsRemote) return (loc.T("remote"), null, true);
        // G-3: номер из каталога карт — в том же виде, что и без карты: «267 (К.кл)», не «267(К.кл)».
        if (map.Building == "ВЦ") return ($"ВЦ {CleanRoom(map.RoomRaw)}", "ГК", false);
        return (CleanRoom(map.RoomRaw), map.Building, false);
    }

    /// <summary>G-3: дата в общем формате глоссария — «пн, 12 окт.».</summary>
    public static string ShortDate(DateTime date) => date.ToString("ddd, d MMM", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
}
