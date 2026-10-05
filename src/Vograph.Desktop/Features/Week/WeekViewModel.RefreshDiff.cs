using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json;
using Vograph.Desktop.Features.Preferences;

namespace Vograph.Desktop.Features.Week;

public sealed partial class WeekViewModel
{
    private sealed record WeekSourceStamp(string Period, int WeekCount, bool Invert,
        string RawSource, string Choices);
    private sealed record RefreshSnapshot(string Scope, DateTime Monday, IReadOnlyList<WeekDay> Days,
        WeekSourceStamp Source);
    private RefreshSnapshot? pendingRefreshSnapshot;
    private WeekSourceStamp? lastRenderedStamp;
    [ObservableProperty] private bool showRefreshDiff;
    [ObservableProperty] private string refreshDiffText = "";
    private string RefreshScope => App.Profile.DatabasePath + ":" + App.Settings.MyGroupId;
    private WeekSourceStamp? ReadWeekSourceStamp()
    {
        var settings = App.Db.GetSettings();
        if (string.IsNullOrWhiteSpace(settings.MyGroupId)) return null;
        var group = settings.MyGroupId;
        var lessons = App.Db.GetAllLessonsForGroup(group);
        var source = StudyImpactPlanner.Fingerprint(settings, lessons, new Dictionary<string, string>());
        var choices = JsonSerializer.Serialize(App.Db.GetSubgroupChoices(group)
            .OrderBy(row => row.Key, StringComparer.Ordinal).ToArray());
        return new(settings.PeriodStart ?? "", settings.WeekCount, settings.ParityInvert, source, choices);
    }

    private void CaptureAndReloadAfterScheduleChange()
    {
        if (Days.Count == 7 && HasCopy && HasGroup && lastRenderedStamp is { } source)
            pendingRefreshSnapshot = new RefreshSnapshot(RefreshScope, Days[0].Date.Date,
                Days.Select(day => day.Day with { Rows = day.Day.Rows.ToArray() }).ToArray(), source);
        _ = ReloadAsync();
    }
    private void ClearRefreshDiff()
    { pendingRefreshSnapshot = null; ShowRefreshDiff = false; RefreshDiffText = ""; }
    [RelayCommand] private void HideRefreshDiff() { ShowRefreshDiff = false; RefreshDiffText = ""; }
    private void ApplyRefreshDiff(WeekModel next, DateTime monday)
    {
        var pending = pendingRefreshSnapshot;
        pendingRefreshSnapshot = null;
        var current = ReadWeekSourceStamp();
        if (pending is null || pending.Scope != RefreshScope || pending.Monday != monday ||
            !next.HasCopy || next.Days.Count != 7 || current is null ||
            pending.Source.Period != current.Period || pending.Source.WeekCount != current.WeekCount ||
            pending.Source.Invert != current.Invert ||
            DateTime.TryParse(App.Settings.PeriodStart, out var start) && monday < start.Date)
        { ClearRefreshDiff(); return; }
        var rawChanged = pending.Source.RawSource != current.RawSource;
        var choiceChanged = pending.Source.Choices != current.Choices;
        if (!rawChanged && !choiceChanged) { ClearRefreshDiff(); return; }
        var reason = rawChanged && choiceChanged ? "обновления расписания и выбора подгруппы" :
            rawChanged ? "обновления расписания" : "выбора подгруппы";
        var changes = WeekComparison.Compare(pending.Days, next.Days);
        RefreshDiffText = changes.Count == 0 ? $"На этой неделе изменений после {reason} нет." :
            $"После {reason}: добавлено {changes.Count(row => row.Kind == "Добавлено")}, " +
            $"убрано {changes.Count(row => row.Kind == "Убрано")}.\n" +
            string.Join("\n", changes.Select(row => $"• {row.Kind} · {monday.AddDays(row.Weekday - 1):dd.MM.yyyy} · " +
                $"{row.Row.Time}–{row.Row.TimeEnd} · {row.Row.SubjectRaw} · {row.Row.TypeLabel} · {row.Row.Room}"));
        ShowRefreshDiff = true;
    }
}
