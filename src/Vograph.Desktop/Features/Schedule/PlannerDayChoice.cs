using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Zapara.Client.Domain;

namespace Vograph.Desktop.Features.Schedule;

public sealed class PlannerDayChoice
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public PlannerDayChoice(PlannerDate day, DateTime selectedDate, ScheduleViewModel owner)
    {
        Date = day.Date;
        IsSelected = Date.Date == selectedDate.Date;
        DayLabel = Date.ToString("ddd", Russian);
        DateLabel = Date.Day.ToString(Russian);
        Workload = day.LessonCount switch { null => "Нет данных", 0 => "Без пар", var count => $"{count} пар" };
        AccessibleName = $"{Date:d MMMM yyyy}, {Workload}";
        SelectCommand = new RelayCommand(() => owner.SelectDate(Date));
    }

    public DateTime Date { get; }
    public bool IsSelected { get; }
    public string DayLabel { get; }
    public string DateLabel { get; }
    public string Workload { get; }
    public string AccessibleName { get; }
    public IRelayCommand SelectCommand { get; }
}

public sealed class PlannerBreak
{
    public PlannerBreak(FreeTimeInterval gap)
    {
        End = gap.End;
        var hours = gap.Minutes / 60;
        var minutes = gap.Minutes % 60;
        var duration = hours > 0 ? $"{hours} ч" + (minutes > 0 ? $" {minutes} мин" : "") : $"{minutes} мин";
        Label = $"Перерыв {gap.Start:hh\\:mm}–{gap.End:hh\\:mm} · {duration}";
    }

    public string Label { get; }
    public TimeSpan End { get; }
}
