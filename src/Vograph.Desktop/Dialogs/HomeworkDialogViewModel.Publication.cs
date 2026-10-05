using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Services;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Dialogs;

public sealed partial class HomeworkDialogViewModel
{
    public HomeworkAudienceSelection Recipients { get; } = new();
    private HomeworkAudienceSnapshot initialRecipients = new(0, "", "");
    [ObservableProperty] private bool loadingRecipients;
    [ObservableProperty] private string recipientNotice = "";
    [ObservableProperty] private bool publicationPending;
    internal HomeworkUpsert? PublicationRequest { get; set; }
    internal Guid? PublicationCommunity { get; set; }
    internal string? PublicationGroupId { get; set; }
    internal Guid PublicationOperationId { get; } = Guid.NewGuid();
    private AppServices? publicationApp;
    public bool ShowRecipients => Share && ShowShare;
    public bool CanDismissEditor => !_aborting && !IsSaving && !IsImporting && !Completion.IsCompleted;
    public DateTimeOffset? PublicationDeadline => _computeDue(Nth) is { } due
        ? new DateTimeOffset(DateTime.SpecifyKind(due.Date.AddDays(1).AddTicks(-1), DateTimeKind.Local)).ToUniversalTime() : null;
    public string PublicationDeadlineLabel => PublicationDeadline is { } deadline ? $"Срок для группы: {deadline.ToLocalTime():dd.MM.yyyy}" : "Срок для группы не указан";
    partial void OnLoadingRecipientsChanged(bool value) => RefreshCanConfirm();
    partial void OnPublicationPendingChanged(bool value) => RefreshEditing();

    internal void ConfigurePublication(AppServices app)
    {
        publicationApp = app;
        Recipients.Changed += RefreshCanConfirm;
        if (ShowShare && CanShare) _ = LoadRecipientsAsync();
    }

    [RelayCommand]
    private async Task LoadRecipientsAsync()
    {
        if (publicationApp is not { } app || IsEdit || LoadingRecipients || PublicationPending || Completion.IsCompleted) return;
        if (app.Communities is null || app.CommunityAccess is null) return;
        LoadingRecipients = true;
        RecipientNotice = "Загружаем учебную группу и получателей…";
        using var operation = app.Work.Enter();
        try
        {
            var groupId = await Task.Run(() => app.Db.GetSettings().MyGroupId ?? "", operation.Token);
            var token = await app.CommunityAccess(operation.Token);
            if (string.IsNullOrWhiteSpace(token) || groupId.Length == 0) { RecipientNotice = "Выберите учебную группу и войдите в аккаунт."; return; }
            var memberships = await app.Communities.ListAsync(token, groupId, operation.Token);
            var membership = memberships.FirstOrDefault(row => !string.IsNullOrWhiteSpace(row.Role));
            if (membership is null) { RecipientNotice = "Вступите в эту учебную группу, чтобы назначать ей задания."; return; }
            var home = await app.Communities.GroupHomeAsync(token, membership.CommunityId, operation.Token);
            var desk = await app.Communities.DeskAsync(token, membership.CommunityId, operation.Token);
            if (!operation.IsCurrent || Completion.IsCompleted) return;
            PublicationCommunity = membership.CommunityId;
            PublicationGroupId = groupId;
            Recipients.Load(membership.CommunityId, home.GroupName ?? home.Name, desk, home.Classmates);
            RecipientNotice = "Общее задание получат выбранные участники. Выполнение каждый отмечает у себя.";
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (operation.IsCurrent && !Completion.IsCompleted) RecipientNotice = "Получатели не загрузились. Повторите загрузку или сохраните задание только себе."; }
        finally { if (operation.IsCurrent) LoadingRecipients = false; }
    }
}

internal sealed class HomeworkPublicationException(string message) : Exception(message);
