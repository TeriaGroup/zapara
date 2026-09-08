using Vograph.Core.Models;

namespace Vograph.Core.Services;

public class IntersectionService
{
    private readonly Database _db;
    public IntersectionService(Database db) => _db = db;

    public record IntersectionResult(string FriendGroupName, string FriendColor, string Teacher, string Room, int Score, bool MatchesThreshold);

    // For each myLesson on selected day, find friend intersections
    public List<IntersectionResult> GetIntersections(Lesson myLesson, DateTime date, List<FriendGroup> friends, int strictness)
    {
        var results = new List<IntersectionResult>();
        if (friends.Count == 0) return results;

        // Determine parity for date
        var settings = _db.GetSettings();
        DateTime periodStart = DateTime.TryParse(settings.PeriodStart, out var ps) ? ps : new DateTime(DateTime.Now.Year, 9, 1);
        int weekCount = settings.WeekCount > 0 ? settings.WeekCount : 2;
        int weekCode = ParityService.GetWeekCode(date, periodStart, weekCount);
        if (settings.ParityInvert) weekCode = weekCode == 1 ? 2 : 1;

        int dow = (int)date.DayOfWeek; if (dow == 0) dow = 7;
        if (dow == 7) return results;

        // Need friend groupId resolution via Name -> Id
        var allGroups = _db.GetAllGroups();
        var groupByName = allGroups.GroupBy(g => g.Name).ToDictionary(g => g.Key, g => g.First().Id);

        foreach (var friend in friends.Where(f => f.Enabled).Take(5))
        {
            if (!groupByName.TryGetValue(friend.GroupName, out var friendGroupId))
            {
                // Try by Id if friend stored Id instead of Name? fallback try direct
                if (_db.GetGroup(friend.GroupName) != null) friendGroupId = friend.GroupName;
                else continue;
            }
            if (!new TimetableApiCache(_db).CanIntersect(myLesson.GroupId, friendGroupId)) continue;
            var friendLessons = _db.GetLessons(friendGroupId, dow, weekCode);
            foreach (var fl in friendLessons)
            {
                if (!TimesOverlap(myLesson.TimeStart, myLesson.TimeEnd, fl.TimeStart, fl.TimeEnd)) continue;
                int score = 0;
                // New gradations per user (2026-09-01): нет на месте (no overlap handled as empty), в вузе, в том же корпусе, на том же этаже, в той же аудитории
                bool sameRoom = !string.IsNullOrWhiteSpace(myLesson.RoomRaw) && !string.IsNullOrWhiteSpace(fl.RoomRaw) && myLesson.RoomRaw.Trim().Equals(fl.RoomRaw.Trim(), StringComparison.OrdinalIgnoreCase);
                bool sameBuilding = !string.IsNullOrWhiteSpace(myLesson.BuildingRaw) && !string.IsNullOrWhiteSpace(fl.BuildingRaw) && myLesson.BuildingRaw.Trim().Equals(fl.BuildingRaw.Trim(), StringComparison.OrdinalIgnoreCase);
                int floorMy = GetFloor(myLesson.RoomRaw);
                int floorFr = GetFloor(fl.RoomRaw);
                bool sameFloor = sameBuilding && floorMy != 0 && floorFr != 0 && floorMy == floorFr;
                if (sameRoom) score = 100; // в той же аудитории
                else if (sameFloor) score = 75; // на том же этаже
                else if (sameBuilding) score = 50; // в том же корпусе
                else score = 25; // в вузе (корпуса в упор, не красный) — same time, different building

                // threshold check
                bool matches = score >= strictness;
                // For threshold 0, any time overlap counts (score 0 >=0 true)
                // For threshold 100, only sameRoom (100) counts
                if (matches)
                {
                    results.Add(new IntersectionResult(friend.GroupName, friend.ColorHex, fl.TeacherRaw, fl.ClassroomRaw, score, matches));
                }
            }
        }
        return results;
    }

    public static bool TimesOverlap(string? startA, string? endA, string? startB, string? endB)
    {
        if (string.IsNullOrWhiteSpace(startA) || string.IsNullOrWhiteSpace(startB)) return false;
        if (!TimeSpan.TryParse(startA, out var sA)) return false;
        if (!TimeSpan.TryParse(startB, out var sB)) return false;
        // Use 95 min if end missing
        TimeSpan eA = TimeSpan.TryParse(endA, out var ea) ? ea : sA.Add(TimeSpan.FromMinutes(95));
        TimeSpan eB = TimeSpan.TryParse(endB, out var eb) ? eb : sB.Add(TimeSpan.FromMinutes(95));
        return sA < eB && sB < eA;
    }

    private static int GetFloor(string? roomRaw)
    {
        if (string.IsNullOrWhiteSpace(roomRaw)) return 0;
        var m = System.Text.RegularExpressions.Regex.Match(roomRaw, @"\d+");
        if (!m.Success) return 0;
        var digits = m.Value;
        if (digits.Length == 0) return 0;
        if (int.TryParse(digits[0].ToString(), out var f) && f >= 1 && f <= 9) return f;
        return 0;
    }

    public static string ScoreToText(int score) => score switch
    {
        100 => "в той же аудитории",
        75 => "на том же этаже",
        50 => "в том же корпусе",
        25 => "в вузе",
        _ => "нет на месте"
    };
}
