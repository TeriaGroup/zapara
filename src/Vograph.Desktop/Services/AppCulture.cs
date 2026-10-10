using System.Globalization;

namespace Vograph.Desktop.Services;

/// <summary>#38: приложение русскоязычное, поэтому даты, дни недели и месяцы в стандартных контролах
/// (CalendarDatePicker, Calendar) показываются по-русски независимо от системной локали
/// (en-US, Invariant без LANG и т. п.): «09.10.2026», а не «10/9/2026».</summary>
public static class AppCulture
{
    public static CultureInfo Russian { get; } = CultureInfo.GetCultureInfo("ru-RU");

    public static void Apply()
    {
        CultureInfo.DefaultThreadCurrentCulture = Russian;
        CultureInfo.DefaultThreadCurrentUICulture = Russian;
        CultureInfo.CurrentCulture = Russian;
        CultureInfo.CurrentUICulture = Russian;
    }
}
