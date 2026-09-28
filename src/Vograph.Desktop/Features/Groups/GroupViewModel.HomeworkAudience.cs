using CommunityToolkit.Mvvm.ComponentModel;
using Vograph.Desktop.Features.Homeworks;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel
{
    public HomeworkAudienceSelection HomeworkRecipients { get; } = new();
    private bool updatingHomeworkRecipients;
    private bool homeworkRecipientsHooked;
    private Guid sharedHomeworkOperationId = Guid.NewGuid();
    private bool sharedHomeworkPending;
    private bool channelHomeworkLoaded;
    public bool ShowSharedHomeworkEditor => CanCreateChannelHomework || editingSharedHomework is not null;
    public bool CanEditSharedHomework => !IsBusy && !sharedHomeworkPending;
    public bool CanSaveSharedHomework => !IsBusy && !PreviewMode && activeHomeworkDraft is not null && HomeworkRecipients.Valid
        && !string.IsNullOrWhiteSpace(SharedHomeworkTitle) && !string.IsNullOrWhiteSpace(SharedHomeworkBody)
        && (editingSharedHomework is { } id ? ChannelHomeworks.Any(row => row.Item.HomeworkId == id && row.Editable) : CanCreateChannelHomework);
    public string SharedHomeworkSaveLabel => sharedHomeworkPending ? "Повторить публикацию" : editingSharedHomework is null ? "Назначить задание" : "Сохранить изменения";
    public IReadOnlyList<string> HomeworkFilters { get; } = ["Все", "Активные", "Готово у меня"];
    [ObservableProperty] private int homeworkFilter;
    public IReadOnlyList<SpaceHomeworkRow> VisibleChannelHomeworks => ChannelHomeworks.Where(row => HomeworkFilter == 0 || row.Completed == (HomeworkFilter == 2)).ToArray();
    public string ChannelHomeworkSummary => channelHomeworkLoaded ? $"Заданий: {ChannelHomeworks.Count} · готово у вас: {ChannelHomeworks.Count(row => row.Completed)}" : "Список заданий ещё не загружен";
    public bool NoVisibleHomework => channelHomeworkLoaded && !IsBusy && VisibleChannelHomeworks.Count == 0;
    partial void OnHomeworkFilterChanged(int value) => NotifyChannelHomeworkState();
    partial void OnSharedHomeworkTitleChanged(string value) => SaveHomeworkDraft();
    partial void OnSharedHomeworkBodyChanged(string value) => SaveHomeworkDraft();
    partial void OnSharedHomeworkDeadlineChanged(string value) => SaveHomeworkDraft();
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task ReloadChannelHomework() => conversationId is { } id && !IsBusy ? LoadSpecializedAsync(id, navigationGeneration) : Task.CompletedTask;

    private void RefreshHomeworkRecipients()
    {
        if (!homeworkRecipientsHooked)
        {
            HomeworkRecipients.Changed += () => { if (!updatingHomeworkRecipients) { SaveHomeworkDraft(); NotifyChannelHomeworkState(); } };
            homeworkRecipientsHooked = true;
        }
        if (communityId is not { } id || desk is null) return;
        updatingHomeworkRecipients = true;
        try { HomeworkRecipients.Load(id, HomeTitle, desk, trustClassmates); }
        finally { updatingHomeworkRecipients = false; NotifyChannelHomeworkState(); }
    }
    private void ClearHomeworkRecipients()
    {
        updatingHomeworkRecipients = true;
        try { HomeworkRecipients.Clear(); sharedHomeworkPending = false; channelHomeworkLoaded = false; }
        finally { updatingHomeworkRecipients = false; NotifyChannelHomeworkState(); }
    }
    private void RestoreHomeworkAudience(HomeworkAudienceSnapshot value)
    {
        updatingHomeworkRecipients = true;
        try { HomeworkRecipients.Restore(value); }
        finally { updatingHomeworkRecipients = false; NotifyChannelHomeworkState(); }
    }
    private void NotifyChannelHomeworkState()
    {
        if (HomeworkRecipients is not null) HomeworkRecipients.Enabled = CanEditSharedHomework;
        foreach (var name in new[] { nameof(ShowSharedHomeworkEditor), nameof(CanEditSharedHomework), nameof(CanSaveSharedHomework),
                     nameof(SharedHomeworkSaveLabel), nameof(VisibleChannelHomeworks), nameof(ChannelHomeworkSummary), nameof(NoVisibleHomework) }) OnPropertyChanged(name);
    }
    private string HomeworkAudienceLabel(HomeworkAudience audience)
    {
        if (audience.Kind == "all") return "Вся учебная группа";
        var roles = audience.RoleIds.Select(id => desk?.Roles.FirstOrDefault(role => role.RoleId == id)?.Name ?? "Подгруппа");
        var people = audience.UserIds.Select(id => trustClassmates.FirstOrDefault(person => person.UserId == id))
            .Select(person => person?.DisplayName ?? person?.Username ?? "Участник");
        return "Получатели: " + string.Join(", ", roles.Concat(people));
    }
}
