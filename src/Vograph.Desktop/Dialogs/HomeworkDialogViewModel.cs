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
    private string _initialText;
    private int _initialNth;
    private bool _initialShare;
    private string[] _initialFiles = [];
    private bool _aborting;

    public HomeworkDialogViewModel(string subjectDisplay, Func<int, DateTime?> computeDue, string? existingText = null, int existingNth = 1)
    {
        _computeDue = computeDue;
        IsEdit = existingText is not null;
        Title = Loc.Current.T(IsEdit ? "hwEditTitle" : "hwTitle");
        SubjectLine = Loc.Current.T("hwSubject", subjectDisplay);
        _text = existingText ?? "";
        _nth = Math.Clamp(existingNth, 1, 10);
        _initialText = _text;
        _initialNth = _nth;
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
    public Func<Task>? PersistAsync { get; set; }
    public long SavedId { get; set; }
    internal bool ShareStarted { get; set; }

    public bool IsDirty => Text != _initialText || Nth != _initialNth || Share != _initialShare ||
        !Files.Select(file => file.Id).Order(StringComparer.Ordinal).SequenceEqual(_initialFiles);
    public bool CanEdit => !_aborting && !IsSaving && !IsImporting && !Completion.IsCompleted;
    public bool CanShareNow => CanEdit && CanShare;
    public string SaveLabel => IsSaving ? "Сохраняем…" : Loc.Current.T("save");

    public void CaptureInitialState()
    {
        _initialText = Text;
        _initialNth = Nth;
        _initialShare = Share;
        _initialFiles = Files.Select(file => file.Id).Order(StringComparer.Ordinal).ToArray();
    }

    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private bool _showDiscardConfirmation;
    [ObservableProperty] private string _error = "";
    public bool HasError => Error.Length > 0;
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnIsSavingChanged(bool value) => RefreshEditing();
    partial void OnIsImportingChanged(bool value) => RefreshEditing();
    partial void OnShowDiscardConfirmationChanged(bool value) => RefreshCanConfirm();

    private void RefreshEditing()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanShareNow));
        OnPropertyChanged(nameof(SaveLabel));
        RefreshCanConfirm();
        IncCommand.NotifyCanExecuteChanged();
        DecCommand.NotifyCanExecuteChanged();
        AddPhotoCommand.NotifyCanExecuteChanged();
        AddDocumentCommand.NotifyCanExecuteChanged();
        RemoveFileCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
        KeepEditingCommand.NotifyCanExecuteChanged();
    }

    public override void Cancel()
    {
        if (!CanEdit) return;
        if (IsDirty) ShowDiscardConfirmation = true;
        else base.Cancel();
    }

    public override void Abort()
    {
        _aborting = true;
        RefreshEditing();
        if (!IsSaving && !IsImporting) base.Abort();
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Discard() { if (CanEdit) base.Cancel(); }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void KeepEditing() { if (CanEdit) ShowDiscardConfirmation = false; }

    protected override void OnConfirm() => _ = SaveAsync();

    public async Task SaveAsync()
    {
        if (!CanConfirm()) return;
        IsSaving = true;
        Error = "";
        try
        {
            if (PersistAsync is not null) await PersistAsync();
            Close(!_aborting);
        }
        catch (Exception)
        {
            Error = "Не удалось сохранить домашку. Текст и файлы остались здесь — попробуйте ещё раз.";
        }
        finally
        {
            IsSaving = false;
            if (_aborting) base.Abort();
        }
    }

    private bool _share;
    public bool Share { get => _share; set { if (CanEdit && (!value || CanShare)) SetProperty(ref _share, value); } }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowShareSignInHint))]
    private bool _canShare;
    private string _text = "";
    public string Text { get => _text; set { if (CanEdit && SetProperty(ref _text, value)) RefreshCanConfirm(); } }
    private int _nth = 1;
    public int Nth { get => _nth; set { if (CanEdit && SetProperty(ref _nth, Math.Clamp(value, 1, 10))) UpdateDue(); } }
    [ObservableProperty] private string _dueText = "";

    partial void OnCanShareChanged(bool value)
    {
        if (!value) Share = false;
        OnPropertyChanged(nameof(CanShareNow));
    }

    protected override bool CanConfirm() => CanEdit && !ShowDiscardConfirmation && !string.IsNullOrWhiteSpace(Text);

    [RelayCommand(CanExecute = nameof(CanEdit))] private void Inc() => Nth = Math.Min(10, Nth + 1);
    [RelayCommand(CanExecute = nameof(CanEdit))] private void Dec() => Nth = Math.Max(1, Nth - 1);

    [RelayCommand(CanExecute = nameof(CanEdit))] private Task AddPhoto() => ImportOne(true);
    [RelayCommand(CanExecute = nameof(CanEdit))] private Task AddDocument() => ImportOne(false);

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void RemoveFile(HomeworkAttachment file)
    {
        if (!CanEdit) return;
        if (file.Staged) DiscardStaged?.Invoke(file.Id);
        // A failed save can leave this staged ID already committed to the saved row.
        Removed.Add(file.Id);
        Files.Remove(file);
    }

    private async Task ImportOne(bool photo)
    {
        if (!CanEdit || Import is null) return;
        if (Files.Count >= HomeworkFileRules.MaxFiles)
        {
            OnTooMany?.Invoke();
            return;
        }
        IsImporting = true;
        Error = "";
        try
        {
            var file = await Import(photo);
            if (file is null) return;
            if (_aborting || Completion.IsCompleted || Files.Count >= HomeworkFileRules.MaxFiles)
            {
                if (file.Staged) DiscardStaged?.Invoke(file.Id);
                return;
            }
            Files.Add(file);
        }
        catch (Exception) { Error = "Не удалось добавить файл. Попробуйте ещё раз."; }
        finally
        {
            IsImporting = false;
            if (_aborting) base.Abort();
        }
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
        dialog.CaptureInitialState();
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
