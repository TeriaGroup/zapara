using Vograph.Core.Services;
using Vograph.Desktop.Services;
using Vograph.Desktop.Dialogs;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Homeworks;

/// <summary>Resolves the signed-in membership, then asks <see cref="GroupHomework.SaveEditorAsync"/> to store the
/// editor pair before any group call. A lookup or send failure still leaves the local row.</summary>
public static class HomeworkShare
{
    public const string UncertainShareNote = "Локально сохранено, проверьте группу: результат отправки не подтверждён. Повторно домашку не отправляем.";
    /// <summary>Called under CoreGate. Remember the row before moving files so a retry updates it.</summary>
    public static void SaveLocal(AppServices app, HomeworkDialogViewModel dialog, string subject, string text, DateTime createdAt)
    {
        if (dialog.SavedId == 0)
            dialog.SavedId = app.Homework.AddHomework(subject, text, dialog.Nth, createdAt: createdAt);
        else
            app.Homework.UpdateHomework(dialog.SavedId, text, dialog.Nth);
        app.HomeworkFiles.Commit(dialog.DraftId, dialog.SavedId, dialog.Removed);
    }

    public static async Task<HomeworkShareOutcome> SaveNewAsync(
        AppServices app, string subject, HomeworkDialogViewModel dialog, Func<Task<string?>> readGroupId, Func<string, string, Task> saveLocal)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(readGroupId);
        ArgumentNullException.ThrowIfNull(saveLocal);
        var text = dialog.Text;
        var share = dialog.Share && dialog.CanShare && !dialog.IsEdit;
        if (dialog.ShareStarted)
        {
            await saveLocal(subject.Trim(), text.Trim()).ConfigureAwait(false);
            return new(true, false, UncertainShareNote);
        }
        var signedIn = false;
        var communityId = "";
        string? token = null;
        if (share && app.Communities is not null && app.CommunityAccess is not null)
        {
            try
            {
                token = await app.CommunityAccess(CancellationToken.None).ConfigureAwait(false);
                signedIn = !string.IsNullOrWhiteSpace(token);
                if (signedIn)
                {
                    var groupId = await readGroupId().ConfigureAwait(false) ?? "";
                    if (groupId.Length > 0)
                    {
                        var list = await app.Communities.ListAsync(token!, groupId).ConfigureAwait(false);
                        communityId = list.FirstOrDefault(item => !string.IsNullOrEmpty(item.Role))?.CommunityId.ToString("D") ?? "";
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                if (string.IsNullOrWhiteSpace(token)) signedIn = false;
                communityId = "";
            }
        }
        try
        {
            var outcome = await GroupHomework.SaveEditorAsync(
                new HomeworkEditorShare(subject, text, share, true),
                signedIn,
                communityId,
                saveLocal,
                async (sentSubject, sentText) =>
                {
                    if (app.Communities is null || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(communityId))
                        throw new InvalidOperationException("group");
                    // Set before dispatch: a timeout/cancellation may occur after the server created the copy.
                    dialog.ShareStarted = true;
                    await app.Communities.ShareHomeworkAsync(token, Guid.Parse(communityId), new HomeworkUpsert(sentSubject, sentText, 0)).ConfigureAwait(false);
                }).ConfigureAwait(false);
            return dialog.ShareStarted && !outcome.Sent ? outcome with { Note = UncertainShareNote } : outcome;
        }
        catch (OperationCanceledException) when (dialog.ShareStarted)
        {
            return new(true, false, UncertainShareNote);
        }
    }
}

internal sealed class LocalHomeworkNotStoredException : Exception;
