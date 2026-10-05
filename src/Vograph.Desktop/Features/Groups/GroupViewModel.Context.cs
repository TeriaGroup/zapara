using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel
{
    private GroupLessonHint? contextLesson;
    private GroupChatContext chatContext = new(null, 0, 0);
    private int contextRequestGeneration;
    [ObservableProperty] private bool contextExpanded = true;

    public bool ShowGroupContext => HasHome && !IsDirect && ShowMessages && chatContext.HasContent;
    public bool HasContextLesson => chatContext.NextLesson is not null;
    public bool HasContextBallots => chatContext.ActiveBallots > 0;
    public bool HasContextUnread => chatContext.Unread > 0;
    public string ContextToggleCaption => ContextExpanded ? "Свернуть" : "Развернуть";
    public string ContextLessonText => chatContext.NextLesson is { } lesson
        ? $"Ближайшая пара по расписанию: {lesson.Date:dd.MM}, {lesson.Time} · {lesson.Subject}" +
            (string.IsNullOrWhiteSpace(lesson.Room) ? "" : $" · {lesson.Room}")
        : "";
    public string ContextBallotsText => $"Активных голосований: {chatContext.ActiveBallots}";
    public string ContextUnreadText => $"Непрочитанных сообщений в каналах: {chatContext.Unread}";
    public string ContextCompactText => string.Join(" · ", new[]
    {
        chatContext.NextLesson is { } lesson ? $"Пара {lesson.Time}" : null,
        chatContext.ActiveBallots > 0 ? $"Голосований: {chatContext.ActiveBallots}" : null,
        chatContext.Unread > 0 ? $"Непрочитано: {chatContext.Unread}" : null
    }.Where(part => part is not null));

    partial void OnContextExpandedChanged(bool value) => OnPropertyChanged(nameof(ContextToggleCaption));

    [RelayCommand]
    private void ToggleContext() => ContextExpanded = !ContextExpanded;

    [RelayCommand]
    private Task OpenContextBallots()
    {
        var channel = Channels.FirstOrDefault(row => !row.IsGlobalBallots && row.Kind == "ballots" &&
            row.ActiveBallots > 0);
        return channel is null ? Task.CompletedTask : OpenChannelAsync(channel);
    }

    private void ResetGroupContext()
    {
        contextRequestGeneration++;
        contextLesson = null;
        chatContext = new(null, 0, 0);
        ContextExpanded = true;
        NotifyGroupContext();
    }

    private void RefreshGroupContext()
    {
        chatContext = GroupChatContext.Compose(Channels.Where(row => !row.IsGlobalBallots)
            .Select(row => new GroupContextChannel(row.TopicId, row.Kind, row.UnreadCount, row.ActiveBallots)),
            contextLesson);
        NotifyGroupContext();
    }

    private void NotifyGroupContext()
    {
        OnPropertyChanged(nameof(ShowGroupContext));
        OnPropertyChanged(nameof(HasContextLesson));
        OnPropertyChanged(nameof(HasContextBallots));
        OnPropertyChanged(nameof(HasContextUnread));
        OnPropertyChanged(nameof(ContextLessonText));
        OnPropertyChanged(nameof(ContextBallotsText));
        OnPropertyChanged(nameof(ContextUnreadText));
        OnPropertyChanged(nameof(ContextCompactText));
    }

    private async Task LoadGroupLessonContextAsync(string? groupName, Guid requestedCommunity)
    {
        var request = ++contextRequestGeneration;
        using var operation = App.Work.Enter();
        if (!operation.IsCurrent) return;
        GroupLessonHint? hint;
        try
        {
            hint = await Task.Run(async () =>
            {
                await App.CoreGate.WaitAsync(operation.Token).ConfigureAwait(false);
                try
                {
                    operation.ThrowIfStale();
                    var selectedId = App.Db.GetSettings().MyGroupId;
                    var selectedName = string.IsNullOrWhiteSpace(selectedId) ? null : App.Db.GetGroup(selectedId)?.Name;
                    return GroupChatContext.ForMatchingGroup(groupName, selectedName, () =>
                    {
                        var (lesson, date) = App.Maps.GetNextLesson(selectedId!, DateTime.Now);
                        return lesson is null ? null : new GroupLessonHint(date, lesson.TimeStart,
                            string.IsNullOrWhiteSpace(lesson.SubjectNormalized) ? lesson.SubjectRaw : lesson.SubjectNormalized,
                            string.IsNullOrWhiteSpace(lesson.RoomRaw) ? lesson.ClassroomRaw : lesson.RoomRaw);
                    });
                }
                finally { App.CoreGate.Release(); }
            });
        }
        catch (OperationCanceledException) { return; }
        catch (ObjectDisposedException) { return; }
        catch (Exception error)
        {
            if (operation.IsCurrent) App.Log.Warn($"Group context unavailable: {error.GetType().Name}");
            return;
        }
        if (!operation.IsCurrent || !CanPublish || request != contextRequestGeneration ||
            communityId != requestedCommunity || !HasHome) return;
        contextLesson = hint;
        RefreshGroupContext();
    }
}
