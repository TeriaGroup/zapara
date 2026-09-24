using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Core.Services;
using Vograph.Desktop.Domain;
using Vograph.Desktop.Services;

namespace Vograph.Desktop.Dialogs;

/// <summary>Create or edit homework: text + "in N lessons of this subject". The due date preview is
/// looked up on every N in a table the caller precomputed off the UI thread — no SQLite from here.</summary>
public sealed partial class HomeworkDialogViewModel : DialogViewModelBase
{
    private readonly Func<int, DateTime?> _computeDue;

    public HomeworkDialogViewModel(string subjectDisplay, Func<int, DateTime?> computeDue, string? existingText = null, int existingNth = 1)
    {
        _computeDue = computeDue;
        IsEdit = existingText is not null;
        Title = Loc.Current.T(IsEdit ? "hwEditTitle" : "hwTitle");
        SubjectLine = Loc.Current.T("hwSubject", subjectDisplay);
        _text = existingText ?? "";
        _nth = Math.Clamp(existingNth, 1, 10);
        UpdateDue();
    }

    public bool IsEdit { get; }
    public bool ShowShare => !IsEdit;
    public bool ShowShareSignInHint => ShowShare && !CanShare;
    public string SubjectLine { get; }
    public string DraftId { get; } = Guid.NewGuid().ToString("N");
    public ObservableCollection<HomeworkAttachment> Files { get; } = new();
    public HashSet<string> Removed { get; } = new(StringComparer.Ordinal);
    public Func<bool, Task<HomeworkAttachment?>>? Import { get; set; }
    public Action<string>? DiscardStaged { get; set; }
    public Action? OnTooMany { get; set; }

    [ObservableProperty] private bool _share;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowShareSignInHint))]
    private bool _canShare;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private int _nth = 1;
    [ObservableProperty] private string _dueText = "";

    partial void OnTextChanged(string value) => RefreshCanConfirm();
    partial void OnNthChanged(int value) => UpdateDue();
    partial void OnCanShareChanged(bool value)
    {
        if (!value) Share = false;
    }

    protected override bool CanConfirm() => !string.IsNullOrWhiteSpace(Text);

    [RelayCommand] private void Inc() => Nth = Math.Min(10, Nth + 1);
    [RelayCommand] private void Dec() => Nth = Math.Max(1, Nth - 1);

    [RelayCommand] private Task AddPhoto() => ImportOne(true);
    [RelayCommand] private Task AddDocument() => ImportOne(false);

    [RelayCommand]
    private void RemoveFile(HomeworkAttachment file)
    {
        if (file.Staged) DiscardStaged?.Invoke(file.Id);
        else Removed.Add(file.Id);
        Files.Remove(file);
    }

    private async Task ImportOne(bool photo)
    {
        if (Import is null) return;
        if (Files.Count >= HomeworkFileRules.MaxFiles)
        {
            OnTooMany?.Invoke();
            return;
        }
        var file = await Import(photo);
        if (file is not null) Files.Add(file);
    }

    private void UpdateDue()
    {
        var loc = Loc.Current;
        var due = _computeDue(Nth);
        DueText = due is null
            ? loc.T("hwNoDate")
            : loc.T("hwDue", $"{DayTitles.ShortDate(due.Value, loc)} ({loc.I18n.FormatDay(due.Value)})");
    }
}

public sealed record HomeworkAttachment(string Id, string Kind, string Name, bool Staged);

public static class HomeworkFilePrompt
{
    public static void Bind(this HomeworkDialogViewModel dialog, AppServices app)
    {
        dialog.CanShare = !app.Profile.IsGuest;
        dialog.DiscardStaged = id => app.HomeworkFiles.DiscardFile(dialog.DraftId, id);
        dialog.OnTooMany = () => app.Toasts.Info(app.Loc.T("hwFileFull"));
        dialog.Import = async photo =>
        {
            var path = await app.FileDialogs.OpenHomeworkAsync(photo);
            if (path is null) return null;
            try
            {
                var kept = dialog.Files.Count(file => !file.Staged);
                var stored = await Task.Run(() => app.HomeworkFiles.Stage(dialog.DraftId, path, photo, kept));
                return new HomeworkAttachment(stored.Id, stored.Kind, stored.Name, true);
            }
            catch (HomeworkFileException ex)
            {
                app.Toasts.Info(app.Loc.T(ex.Code switch { "big" => "hwFileBig", "full" => "hwFileFull", _ => "hwFileBad" }));
                return null;
            }
        };
    }
}
