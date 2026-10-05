namespace Vograph.Desktop.Features.Groups;

public static class GroupBallotBrowse
{
    public static IReadOnlyList<GroupBallotRow> Filter(IEnumerable<GroupBallotRow> rows, string query,
        int statusIndex, int sortIndex)
    {
        var needle = query.Trim();
        statusIndex = statusIndex is >= 1 and <= 3 ? statusIndex : 0;
        sortIndex = sortIndex is >= 1 and <= 2 ? sortIndex : 0;
        var matches = rows.Where(row => (statusIndex == 0 || row.StatusCode == Status(statusIndex))
                && (needle.Length == 0 || row.Question.Contains(needle, StringComparison.OrdinalIgnoreCase)
                    || row.Options.Any(option => option.Text.Contains(needle, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        return sortIndex switch
        {
            1 => matches.OrderBy(row => row.DeadlineAt).ToArray(),
            2 => matches.OrderByDescending(row => row.DeadlineAt).ToArray(),
            _ => matches
        };
    }

    public static int Percent(int votes, long total)
        => total <= 0 ? 0 : (int)Math.Clamp(Math.Round((decimal)Math.Max(0, votes) * 100 / total,
            0, MidpointRounding.AwayFromZero), 0m, 100m);

    public static string Urgency(string status, DateTimeOffset deadline, DateTimeOffset now)
    {
        if (status is not ("open" or "collecting")) return "";
        if (deadline <= now) return "Срок истёк, ожидаем обновление";
        return deadline <= now.AddHours(24) ? "Скоро завершится" : "";
    }

    public static string Summary(GroupBallotRow row)
    {
        var lines = new List<string> { row.Question, row.Status,
            "До " + row.DeadlineAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm") };
        if (row.IsCollecting) lines.Add(row.Supporters);
        lines.Add("Доли от поданных голосов:");
        lines.AddRange(row.Options.Select(option =>
            $"{option.Text}: {option.Votes} {VoteWord(option.Votes)} · {option.Percent}% голосов"));
        if (row.Outcome.Length > 0) lines.Add(row.Outcome);
        return string.Join(Environment.NewLine, lines);
    }

    private static string Status(int index) => index switch
    {
        1 => "collecting", 2 => "open", 3 => "closed", _ => ""
    };

    private static string VoteWord(int count)
    {
        var lastTwo = count % 100;
        if (lastTwo is >= 11 and <= 14) return "голосов";
        return (count % 10) switch { 1 => "голос", >= 2 and <= 4 => "голоса", _ => "голосов" };
    }
}
