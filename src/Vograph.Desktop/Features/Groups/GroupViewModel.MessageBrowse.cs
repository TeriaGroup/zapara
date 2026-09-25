using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Vograph.Desktop.Features.Groups;

public sealed partial class GroupViewModel
{
    private const string CopiedTextStatus = "Текст скопирован.";
    private const string CopyFailedStatus = "Не удалось скопировать текст.";
    private IReadOnlyList<GroupMessageRow> filteredMessages = [];
    private Guid? pendingDeleteMessageId;

    [ObservableProperty] private string messageSearch = "";
    [ObservableProperty] private int messageAuthorIndex;
    [ObservableProperty] private int messageKindIndex;
    [ObservableProperty] private bool loadingOlder;
    [ObservableProperty] private bool showMessageBrowse;

    public IReadOnlyList<GroupMessageRow> FilteredMessages => filteredMessages;
    public bool HasMessageFilters => MessageSearch.Trim().Length > 0
        || MessageAuthorIndex is >= 1 and <= 2 || MessageKindIndex is >= 1 and <= 4;
    public bool NoMessageMatches => HasMessageFilters && filteredMessages.Count == 0;
    public bool NoMessages => Messages.Count == 0 && !HasMessageFilters;
    public string MessageResultCount => $"Показано {filteredMessages.Count} из {Messages.Count} загруженных";
    public string MessageBrowseCaption => ShowMessageBrowse ? "Скрыть поиск и фильтры"
        : HasMessageFilters ? $"Поиск и фильтры · {MessageResultCount}" : "Поиск и фильтры";
    public string OlderCaption => LoadingOlder ? "Загрузка…" : "Загрузить ранние";
    public bool CanLoadOlder => HasMore && !LoadingOlder;
    public bool HasHoldAction => HoldCaption.Length > 0;
    public bool HasPendingDeleteMessage => pendingDeleteMessageId is not null;
    public bool ShowRetryBrowse => Status.Length > 0 && Status is not (CopiedTextStatus or CopyFailedStatus);

    partial void OnMessageSearchChanged(string value) => RefreshMessageBrowse();
    partial void OnMessageAuthorIndexChanged(int value) => RefreshMessageBrowse();
    partial void OnMessageKindIndexChanged(int value) => RefreshMessageBrowse();
    partial void OnHasMoreChanged(bool value) => OnPropertyChanged(nameof(CanLoadOlder));
    partial void OnLoadingOlderChanged(bool value)
    {
        OnPropertyChanged(nameof(OlderCaption));
        OnPropertyChanged(nameof(CanLoadOlder));
    }
    partial void OnHoldCaptionChanged(string value) => OnPropertyChanged(nameof(HasHoldAction));
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(ShowRetryBrowse));
    partial void OnShowMessageBrowseChanged(bool value) => OnPropertyChanged(nameof(MessageBrowseCaption));

    [RelayCommand]
    private void ToggleMessageBrowse() => ShowMessageBrowse = !ShowMessageBrowse;

    [RelayCommand]
    private void ResetMessageFilters()
    {
        MessageSearch = "";
        MessageAuthorIndex = 0;
        MessageKindIndex = 0;
    }

    private void RefreshMessageBrowse()
    {
        filteredMessages = GroupMessageBrowse.Filter(Messages, MessageSearch, MessageAuthorIndex, MessageKindIndex);
        GroupMessageRow? previous = null;
        foreach (var row in filteredMessages)
        {
            var date = row.CreatedAt.ToLocalTime().Date;
            var startsDay = previous is null || previous.CreatedAt.ToLocalTime().Date != date;
            row.DayHeader = row.CreatedAt == default || !startsDay ? ""
                : date.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));
            row.ShowAuthor = !row.Mine && (previous is null || startsDay || previous.SenderId != row.SenderId);
            previous = row;
        }
        OnPropertyChanged(nameof(FilteredMessages));
        OnPropertyChanged(nameof(HasMessageFilters));
        OnPropertyChanged(nameof(NoMessageMatches));
        OnPropertyChanged(nameof(NoMessages));
        OnPropertyChanged(nameof(MessageResultCount));
        OnPropertyChanged(nameof(MessageBrowseCaption));
    }

    private async Task CopyMessageAsync(GroupMessageRow row)
    {
        if (!Messages.Contains(row) || GroupMessageBrowse.CopyText(row) is not { } content) return;
        try
        {
            if (clipboardWriter is null) throw new InvalidOperationException("Clipboard unavailable");
            await clipboardWriter(content);
            Status = CopiedTextStatus;
        }
        catch (Exception)
        {
            Status = CopyFailedStatus;
        }
    }

    [RelayCommand]
    private void CancelHoldAction()
    {
        replyTo = null;
        editing = null;
        HoldCaption = "";
        if (conversationId is Guid id) Draft = channelDrafts.GetValueOrDefault(DraftKey(id)) ?? "";
    }

    private void AskDeleteMessage(GroupMessageRow row)
    {
        pendingDeleteMessageId = row.Id;
        OnPropertyChanged(nameof(HasPendingDeleteMessage));
    }

    [RelayCommand]
    private void CancelDeleteMessage()
    {
        pendingDeleteMessageId = null;
        OnPropertyChanged(nameof(HasPendingDeleteMessage));
    }

    [RelayCommand]
    private Task ConfirmDeleteMessage()
    {
        var row = Messages.FirstOrDefault(item => item.Id == pendingDeleteMessageId && !item.Deleted);
        CancelDeleteMessage();
        return row is null ? Task.CompletedTask : ChangeHeldMessageAsync(row, true, null);
    }
}
