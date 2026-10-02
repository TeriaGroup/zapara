using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Features.Week;

public sealed partial class WeekViewModel
{
    private DateTime? comparisonBaseMonday;
    [ObservableProperty] private DateTime? compareWeekDate;
    [ObservableProperty] private bool showWeekComparison;
    [ObservableProperty] private string comparisonText = "";

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task CompareWeeks()
    {
        if (!ShowWeekCards || Days.Count != 7 || CompareWeekDate is null) return;
        var baseMonday = Days[0].Date.Date;
        var otherMonday = CompareWeekDate.Value.Date.AddDays(-((int)CompareWeekDate.Value.DayOfWeek + 6) % 7);
        if (DateTime.TryParse(App.Settings.PeriodStart, out var periodStart) &&
            (baseMonday < periodStart.Date || otherMonday < periodStart.Date))
        { ComparisonText = "Недели до начала учебного периода нельзя сравнить: расписание для них неизвестно.";
            ShowWeekComparison = true; return; }
        if (baseMonday == otherMonday)
        { ComparisonText = "Выберите другую неделю для сравнения."; ShowWeekComparison = true; return; }
        var scope = App.Profile.DatabasePath + ":" + App.Settings.MyGroupId;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var model = await RunAsync(() => _composer.ComposeCalendar(otherMonday, _clock().Date), "week comparison");
        if (model is null || !operation.IsCurrent || scope != App.Profile.DatabasePath + ":" + App.Settings.MyGroupId ||
            Days.Count != 7 || Days[0].Date.Date != baseMonday) return;
        if (!model.HasCopy)
        { ComparisonText = "Для выбранной недели нет сохранённого расписания."; ShowWeekComparison = true; return; }
        var changes = WeekComparison.Compare(Days.Select(day => day.Day), model.Days);
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        var labels = changes.Select(change => $"{change.Kind} · {culture.DateTimeFormat.GetDayName((DayOfWeek)(change.Weekday % 7))} · " +
            $"{change.Row.Time}–{change.Row.TimeEnd} · {change.Row.Name} · {change.Row.TypeLabel} · {change.Row.Room}");
        ComparisonText = $"Сравнение {baseMonday:dd.MM.yyyy} и {otherMonday:dd.MM.yyyy}: " +
            (changes.Count == 0 ? "различий между выбранными неделями нет." :
                $"добавлено {changes.Count(change => change.Kind == "Добавлено")}, убрано {changes.Count(change => change.Kind == "Убрано")}.") +
            (changes.Count > 0 ? "\n" + string.Join("\n", labels) : "");
        comparisonBaseMonday = baseMonday;
        ShowWeekComparison = true;
    }

    [RelayCommand]
    private void HideWeekComparison()
    { ShowWeekComparison = false; ComparisonText = ""; comparisonBaseMonday = null; }

    private void ValidateWeekComparison(DateTime monday)
    { if (comparisonBaseMonday is { } old && old != monday) HideWeekComparison(); }
}
