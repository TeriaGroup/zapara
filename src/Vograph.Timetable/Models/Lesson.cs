namespace Vograph.Core.Models;

public class Lesson
{
    public long Id { get; set; }
    public string GroupId { get; set; } = "";
    public int DayOfWeek { get; set; } // 1=Monday .. 6=Saturday
    public int Parity { get; set; } // 0=both, 1=odd, 2=even
    public int Index { get; set; } // 1..7 order within day+parity
    public string TimeStart { get; set; } = ""; // HH:mm
    public string TimeEnd { get; set; } = ""; // HH:mm, derived +95m
    public string SubjectRaw { get; set; } = ""; // full Discipline e.g. "лек ВЫСШ. МАТЕМАТ"
    public string SubjectNormalized { get; set; } = ""; // for overrides key
    public string TeacherRaw { get; set; } = ""; // joined ShortName
    public string RoomRaw { get; set; } = ""; // e.g. "493"
    public string BuildingRaw { get; set; } = ""; // e.g. "ВЦ" or "*"
    public string TypeRaw { get; set; } = ""; // e.g. "лек", "пр", "лаб"
    public string ClassroomRaw { get; set; } = ""; // original Classroom string
}
