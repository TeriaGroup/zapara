using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Chat;

public sealed partial class ChatInboxViewModel
{
    // This section belongs to one AppServices/profile; nothing is persisted or shared between accounts.
    private sealed record ComposerState(string Text, Guid? Reply, Guid? Edit, string Ordinary, Guid? OrdinaryReply, long Revision, string Error);
    private readonly Dictionary<Guid, ComposerState> composers = [];
    private readonly HashSet<Guid> sendingConversations = [];
    private readonly Dictionary<Guid, long> messageHistoryVersions = [];
    private int messageLoadSerial;
    private bool restoringComposer;
    private string ordinaryDraft = "";
    private Guid? ordinaryReply;
    private long composerRevision;
    [ObservableProperty] private string composerError = "";
    [ObservableProperty] private bool sending;
    public bool HasComposerAction => replyTo is not null || editing is not null;
    public string SendCaption => Sending ? "Отправка…" : editing is not null ? "Сохранить" : "Отправить";
    private static string NormalizeBody(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    private int DraftCharacterCount => NormalizeBody(Draft).EnumerateRunes().Count();
    public string DraftLengthHint => DraftCharacterCount >= 1800 ? $"{DraftCharacterCount} / 2000" : "";
    public string DraftValidation
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Draft)) return "";
            try { CommunityValidation.Message(NormalizeBody(Draft)); return ""; }
            catch (ArgumentException)
            {
                return DraftCharacterCount > 2000 ? "Сообщение слишком длинное. Максимум — 2000 символов."
                    : "В сообщении есть недопустимые символы. Удалите их перед отправкой.";
            }
        }
    }
    public bool NoConversation => !HasConversation;
    public bool NoMessages => HasConversation && !LoadingMessages && MessagesLoaded && !MessageLoadFailed && Messages.Count == 0;
    [ObservableProperty] private bool loadingMessages;
    [ObservableProperty] private bool messagesLoaded;
    [ObservableProperty] private bool messageLoadFailed;
    [ObservableProperty] private bool inboxLoadFailed;
    [ObservableProperty] private bool inboxLoaded;
    partial void OnInboxLoadedChanged(bool value) => RefreshInboxBrowse();
    partial void OnLoadingMessagesChanged(bool value) { OnPropertyChanged(nameof(NoMessages)); RefreshMessageBrowse(); }
    partial void OnMessagesLoadedChanged(bool value) { OnPropertyChanged(nameof(NoMessages)); RefreshMessageBrowse(); }
    partial void OnMessageLoadFailedChanged(bool value) { OnPropertyChanged(nameof(NoMessages)); RefreshMessageBrowse(); }
    partial void OnInboxLoadFailedChanged(bool value) => RefreshInboxBrowse();
    partial void OnSendingChanged(bool value) => NotifyComposer();
    partial void OnDraftChanged(string value)
    {
        if (!restoringComposer) { composerRevision++; SaveComposer(); }
        NotifyComposer();
    }
    private void NotifyComposer()
    {
        OnPropertyChanged(nameof(HasComposerAction));
        OnPropertyChanged(nameof(SendCaption));
        OnPropertyChanged(nameof(DraftLengthHint));
        OnPropertyChanged(nameof(DraftValidation));
        SendCommand.NotifyCanExecuteChanged();
        AttachCommand.NotifyCanExecuteChanged();
        StartRecordingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanAttachMedia));
    }
    private bool CanSend() => HasConversation && !Sending && !IsRecording && !IsFinalizingRecording
        && !string.IsNullOrWhiteSpace(Draft) && DraftValidation.Length == 0 && App.Work.CanPublish;
    private bool CanAttach(string? kind) => kind is ("image" or "file")
        && HasConversation && !Sending && !IsRecording && !IsFinalizingRecording && editing is null && App.Work.CanPublish;
    private long AdvanceMessageHistory(Guid id) => messageHistoryVersions[id] = messageHistoryVersions.GetValueOrDefault(id) + 1;
    private bool CurrentMessageHistory(Guid id, long version) => messageHistoryVersions.GetValueOrDefault(id) == version;
    private ComposerState CurrentComposer() => new(Draft, replyTo, editing, ordinaryDraft, ordinaryReply, composerRevision, ComposerError);
    private void SaveComposer()
    {
        if (conversationId is Guid id && !restoringComposer) composers[id] = CurrentComposer();
    }
    private void RestoreComposer(Guid id)
    {
        var state = composers.GetValueOrDefault(id) ?? new("", null, null, "", null, 0, "");
        restoringComposer = true;
        try
        {
            replyTo = state.Reply; editing = state.Edit; ordinaryDraft = state.Ordinary;
            ordinaryReply = state.OrdinaryReply;
            composerRevision = state.Revision; Draft = state.Text; ComposerError = state.Error;
            ActionCaption = editing is not null ? "Редактирование" : replyTo is not null ? "Ответ" : "";
            Sending = sendingConversations.Contains(id);
        }
        finally { restoringComposer = false; NotifyComposer(); }
    }
    private void BeginReply(Guid message)
    {
        if (editing is not null) Draft = ordinaryDraft;
        replyTo = message; editing = null; ordinaryDraft = ""; ordinaryReply = null; ActionCaption = "Ответ";
        composerRevision++; SaveComposer(); NotifyComposer();
    }
    private void BeginEdit(Guid message, string body)
    {
        if (editing is null) { ordinaryDraft = Draft; ordinaryReply = replyTo; }
        editing = message; replyTo = null; ActionCaption = "Редактирование";
        Draft = body; composerRevision++; SaveComposer(); NotifyComposer();
    }
    [RelayCommand]
    private void CancelComposerAction()
    {
        var restore = editing is not null;
        replyTo = restore ? ordinaryReply : null; editing = null;
        ActionCaption = replyTo is not null ? "Ответ" : "";
        if (restore) Draft = ordinaryDraft;
        ordinaryDraft = ""; ordinaryReply = null; composerRevision++; SaveComposer(); NotifyComposer();
    }
}
