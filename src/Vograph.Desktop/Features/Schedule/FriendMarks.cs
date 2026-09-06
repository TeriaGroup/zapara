using Vograph.Core.Models;
using Vograph.Desktop.Controls;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Features.Schedule;

/// <summary>Friend dots for one lesson — shared by the day cards and the Friends preview. DB-bound: call under RunAsync.</summary>
public static class FriendMarks
{
    public static IReadOnlyList<FriendMark> Compute(AppServices app, Lesson l, DateTime date, IReadOnlyList<FriendGroup> friends, Settings settings, Loc loc)
    {
        var enabled = friends.Where(f => f.Enabled).Take(5).ToList();
        if (enabled.Count == 0) return Array.Empty<FriendMark>();
        // strictness 0 → every time overlap; the visibility threshold is applied below.
        var results = app.Intersections.GetIntersections(l, date, enabled, strictness: 0);
        var marks = new List<FriendMark>();
        foreach (var f in enabled)
        {
            var best = results.Where(r => r.FriendGroupName == f.GroupName).Select(r => r.Score).DefaultIfEmpty(0).Max();
            var present = best > 0 && best >= settings.IntersectionStrictness;
            if (!present && !settings.AlwaysShowAllTrafficLights) continue;
            var where = present
                ? loc.T(best switch { >= 100 => "inter100", >= 75 => "inter75", >= 50 => "inter50", _ => "inter25" })
                : loc.T("friendAbsent");
            var names = string.IsNullOrWhiteSpace(f.MemberNames) ? "" : $" ({f.MemberNames})";
            marks.Add(new FriendMark(f.GroupName, f.MemberNames, FriendPalette.IndexOf(f.ColorHex), present ? FriendDot.FromScore(best) : DotFill.Off, $"{f.GroupName}{names} · {where}"));
        }
        return marks;
    }
}
