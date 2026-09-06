using System.Text.RegularExpressions;
using Vograph.Core.Models;
using Vograph.Core.Services;

namespace Vograph.Desktop.Features.Teachers;

public static class TeacherSearch
{
    private static readonly Regex Initial = new(@"\b(\p{L})\.", RegexOptions.Compiled);

    /// <summary>«Барт Елена Леонидовна» / «Барт Е.Л.» vs the group XML's «Барт Е.Л.»: same surname, and every
    /// initial the short form carries matches the corresponding name part. A short form without initials matches by surname alone.
    /// The short form decides how many leading tokens are the surname — a lecturer record may spell the given names out
    /// («Барт Елена Леонидовна»), so its own split cannot be guessed from its shape.</summary>
    public static bool SameTeacher(string lecturerName, string shortName)
    {
        var (surname, wanted) = Parts(shortName);
        if (surname.Length == 0) return false;
        var tokens = lecturerName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < surname.Length) return false;
        for (var i = 0; i < surname.Length; i++)
            if (!tokens[i].TrimEnd('.').Equals(surname[i], StringComparison.OrdinalIgnoreCase)) return false;
        var have = Initials(tokens.Skip(surname.Length));
        for (var i = 0; i < wanted.Count; i++)
        {
            if (i >= have.Count) return true; // the lecturer record is shorter than the short form: surname decided
            if (!have[i].Equals(wanted[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>Surname = every token before the first initial-like token («Аббу Фадда» in «Аббу Фадда Т.М.»); initials = first letters of the rest.</summary>
    private static (string[] Surname, IReadOnlyList<string> Initials) Parts(string name)
    {
        var tokens = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var split = 0;
        while (split < tokens.Length && !Initial.IsMatch(tokens[split]) && tokens[split].Length > 2) split++;
        return (tokens.Take(split).Select(t => t.TrimEnd('.')).ToArray(), Initials(tokens.Skip(split)));
    }

    /// <summary>«Е.Л.» → Е, Л; a spelled-out given name → its first letter.</summary>
    private static IReadOnlyList<string> Initials(IEnumerable<string> tokens)
    {
        var initials = new List<string>();
        foreach (var t in tokens)
        {
            if (Initial.IsMatch(t)) foreach (Match m in Initial.Matches(t)) initials.Add(m.Groups[1].Value);
            else if (t.Length > 0) initials.Add(t[..1]);
        }
        return initials;
    }

    /// <summary>Lecturer ids of everyone who teaches my group, matched from schedule_cache's TeacherRaw («Барт Е.Л.; Иванов С.П.»).</summary>
    public static HashSet<string> MyLecturerIds(IEnumerable<Lesson> myLessons, IReadOnlyList<LecturerInfo> lecturers)
    {
        var shorts = myLessons
            .Where(l => !string.IsNullOrWhiteSpace(l.TeacherRaw) && l.TeacherRaw != "—")
            .SelectMany(l => l.TeacherRaw.Split(';'))
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var lect in lecturers)
            if (shorts.Any(s => SameTeacher(lect.Name, s))) ids.Add(lect.Id);
        return ids;
    }
}

/// <summary>Lessons grouped per lecturer once per load; filtering 700+ lecturers stays instant.</summary>
public sealed class TeacherIndex
{
    private readonly Dictionary<string, List<LecturerLesson>> _byLecturer;

    public TeacherIndex(IReadOnlyList<LecturerInfo> lecturers, IReadOnlyList<LecturerLesson> lessons)
    {
        Lecturers = lecturers.OrderBy(l => l.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"), ignoreCase: true)).ToList();
        _byLecturer = lessons.GroupBy(l => l.LecturerId).ToDictionary(g => g.Key, g => g.ToList());
    }

    public IReadOnlyList<LecturerInfo> Lecturers { get; }

    public IReadOnlyList<LecturerLesson> LessonsOf(string lecturerId) =>
        _byLecturer.TryGetValue(lecturerId, out var l) ? l : Array.Empty<LecturerLesson>();

    /// <summary>Query matches name, department or any discipline the lecturer teaches (the old subject combo box folded into search).</summary>
    public List<LecturerInfo> Filter(string query, bool onlyMine, ISet<string> myIds)
    {
        var q = query.Trim();
        IEnumerable<LecturerInfo> res = Lecturers;
        if (onlyMine) res = res.Where(l => myIds.Contains(l.Id));
        if (q.Length > 0)
            res = res.Where(l =>
                l.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                l.Kafedra.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                LessonsOf(l.Id).Any(x => x.DisciplineRaw.Contains(q, StringComparison.OrdinalIgnoreCase)));
        return res.ToList();
    }
}
