using System.Text;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Zapara.Client.Domain;

namespace Vograph.Desktop.Features.Homeworks;

public sealed partial class HomeworkViewModel
{
    [ObservableProperty] private bool showFilteredPlanPreview;
    [ObservableProperty] private string filteredPlanPreview = "";
    private string CurrentFilteredPlanText() => HomeworkTransferText.Format(Groups.SelectMany(group => group.Items)
        .Select(row => new HomeworkTransferItem(row.Subject, row.Text, row.Entry.Due, row.IsDone,
            row.Files.Select(file => file.Name).ToArray())).ToArray());
    internal void RefreshFilteredPlanPreview()
    { if (ShowFilteredPlanPreview) FilteredPlanPreview = CurrentFilteredPlanText(); }
    [RelayCommand] private void PreviewFilteredPlan()
    { FilteredPlanPreview = CurrentFilteredPlanText(); ShowFilteredPlanPreview = true; }
    [RelayCommand] private void HideFilteredPlan() => ShowFilteredPlanPreview = false;
    [RelayCommand]
    private async Task CopyFilteredPlan()
    {
        var content = CurrentFilteredPlanText();
        FilteredPlanPreview = content;
        try
        {
            if (clipboardWriter is null) throw new InvalidOperationException("Clipboard unavailable");
            await clipboardWriter(content);
            App.Toasts.Info("Текущий список заданий скопирован.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { App.Toasts.Error("Не удалось скопировать список заданий."); }
    }
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ExportDeadlines()
    {
        var rows = Groups.SelectMany(group => group.Items).ToArray();
        if (rows.Length == 0) { App.Toasts.Info("В текущем списке нет личных заданий для календаря."); return; }
        var groupId = App.Settings.MyGroupId;
        var scope = CurrentBrowseScope();
        var entries = rows.Select(row => new AllDayCalendarEntry(
            "homework:" + CalendarExport.CanonicalId(groupId, row.Entry.SubjectRaw, row.Text, ""),
            row.Entry.Due is { } due ? DateOnly.FromDateTime(due.Date) : null,
            $"{row.Subject}: {row.Text}", "Личное домашнее задание"));
        var result = CalendarExport.CreateAllDay(entries, "Домашние задания · Расписание военмех", DateTimeOffset.UtcNow);
        if (result.EventCount == 0)
        { App.Toasts.Info("У видимых личных заданий нет срока для календаря."); return; }
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        var path = await App.FileDialogs.SaveCalendarAsync($"domashka-voenmeh-{_clock():yyyyMMdd}.ics");
        if (path is null || !operation.IsCurrent || CurrentBrowseScope() != scope) return;
        try
        {
            await File.WriteAllTextAsync(path, result.Content, new UTF8Encoding(false), operation.Token);
            if (operation.IsCurrent) App.Toasts.Info($"Сохранено сроков: {result.EventCount}; без срока или повторов: {result.SkippedCount}.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { if (operation.IsCurrent) App.Toasts.Error("Не удалось сохранить календарь домашки."); }
    }
}
