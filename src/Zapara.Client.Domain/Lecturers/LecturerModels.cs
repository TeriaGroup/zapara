namespace Vograph.Core.Services;

// Represents a lecturer from TimetableLecturer50.xml
public class LecturerInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = ""; // LecturerName
    public string Kafedra { get; set; } = "";
    public string ShortName { get; set; } = ""; // derived or from group XML
}

public class LecturerLesson
{
    public string LecturerId { get; set; } = "";
    public string LecturerName { get; set; } = "";
    public string Kafedra { get; set; } = "";
    public int DayOfWeek { get; set; } // 1..6
    public int Parity { get; set; } // 1 odd, 2 even
    public string TimeStart { get; set; } = "";
    public string TimeEnd { get; set; } = "";
    public string DisciplineRaw { get; set; } = ""; // full Discipline
    public string TypeRaw { get; set; } = "";
    public string SubjectRaw { get; set; } = ""; // without type
    public string SubjectNormalized { get; set; } = "";
    public string ClassroomRaw { get; set; } = "";
    public string RoomRaw { get; set; } = "";
    public string BuildingRaw { get; set; } = "";
    public List<GroupRef> Groups { get; set; } = new();
}

public class GroupRef
{
    public string IdGroup { get; set; } = "";
    public string Number { get; set; } = "";
}
