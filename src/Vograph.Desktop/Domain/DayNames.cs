namespace Vograph.Desktop.Domain;

/// <summary>I18n keys of the weekdays, indexed the way Core numbers them (1 = Monday … 7 = Sunday).</summary>
public static class DayNames
{
    private static readonly string[] Keys = { "mon", "tue", "wed", "thu", "fri", "sat", "sun" };

    public static string Key(int dow) => Keys[Math.Clamp(dow, 1, 7) - 1];

    /// <summary>«monShort» → «Пн».</summary>
    public static string ShortKey(int dow) => Key(dow) + "Short";
}
