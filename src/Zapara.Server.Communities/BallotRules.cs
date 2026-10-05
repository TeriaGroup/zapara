namespace Zapara.Server.Communities;

public static class BallotRules
{
    public const int ActiveLimit = 5;
    public const int MinimumGroup = 3;
    public const string WeekQuestion = "Как прошла учебная неделя?";
    public static IReadOnlyList<string> WeekOptions { get; } = new[] { "Легко", "Обычно", "Тяжело" };

    public static int SupportersNeeded(int members)
    {
        if (members < 1) return 1;
        var quarter = (int)(((long)members + 3) / 4);
        var need = quarter < 3 ? 3 : quarter;
        return need > members ? members : need;
    }
}
