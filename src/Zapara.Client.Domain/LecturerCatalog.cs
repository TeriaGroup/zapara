using Vograph.Core.Models;
using Vograph.Core.Services;
using Vograph.Desktop.Features.Teachers;

namespace Zapara.Client.Domain;

public sealed class LecturerCatalog
{
    private readonly TeacherIndex _index;
    private LecturerCatalog(IReadOnlyList<LecturerInfo> lecturers, IReadOnlyList<LecturerLesson> lessons)
    {
        _index = new TeacherIndex(lecturers, lessons);
        Lecturers = _index.Lecturers;
        Lessons = lessons;
    }

    public IReadOnlyList<LecturerInfo> Lecturers { get; }
    public IReadOnlyList<LecturerLesson> Lessons { get; }
    public static LecturerCatalog Parse(string xml)
    {
        var parsed = LecturerXmlParser.Parse(xml);
        return new(parsed.Lecturers, parsed.Lessons);
    }

    public IReadOnlyList<LecturerInfo> Search(string query, bool onlyMine = false, IEnumerable<Lesson>? myLessons = null) =>
        _index.Filter(query, onlyMine, TeacherSearch.MyLecturerIds(myLessons ?? [], Lecturers));

    public IReadOnlyList<LecturerLesson> LessonsOf(string lecturerId, int parity = 0, bool invert = false)
    {
        var code = invert && parity != 0 ? parity == 1 ? 2 : 1 : parity;
        return _index.LessonsOf(lecturerId).Where(l => code == 0 || l.Parity == 0 || l.Parity == code)
            .OrderBy(l => l.DayOfWeek).ThenBy(l => l.TimeStart).ThenBy(l => l.Parity).ToArray();
    }

    public static bool SameTeacher(string lecturerName, string shortName) => TeacherSearch.SameTeacher(lecturerName, shortName);
}
