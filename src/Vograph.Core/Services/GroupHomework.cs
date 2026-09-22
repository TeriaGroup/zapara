namespace Vograph.Core.Services;

public sealed record HomeworkShareInput(bool Share, bool SignedIn, string? CommunityId);

/// <summary>What the homework editor holds when the person presses save: the subject and the text they wrote,
/// whether the group box is on, and whether this is a new task. An edit does not send another group copy.</summary>
public sealed record HomeworkEditorShare(string Subject, string Text, bool Share, bool IsNew);

public sealed record HomeworkShareOutcome(bool Stored, bool Sent, string Note);

public sealed record LocalHomeworkMark(string Id, bool Done);

public sealed record GroupCopyMark(string HomeworkId, string MemberId, bool Completed);

public sealed record GroupHomeworkBook(IReadOnlyList<LocalHomeworkMark> Local, IReadOnlyList<GroupCopyMark> Copies);

/// <summary>Local homework is stored first. A group copy is sent only when the box is on, the person is signed in,
/// and the timetable group already has a community. One member's «Сделано» touches only that member's copy.</summary>
public static class GroupHomework
{
    public const string SignInNote = "Войдите в аккаунт, чтобы отправить домашку группе.";
    public const string LocalOnlyNote = "Вы ещё не в группе. Домашка сохранена только на этом устройстве.";
    public const string FailedNote = "На устройстве сохранено. Группе отправить не получилось.";
    public const string SharedNote = "Домашка продублирована всей группе.";

    /// <summary>Store the editor's subject and text on the device first, then submit that same pair to the group
    /// only when the box is on, the task is new, the person is signed in, and a membership is present.</summary>
    public static Task<HomeworkShareOutcome> SaveEditorAsync(
        HomeworkEditorShare editor, bool signedIn, string? communityId, Func<string, string, Task> saveLocal, Func<string, string, Task> send)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(saveLocal);
        ArgumentNullException.ThrowIfNull(send);
        var subject = (editor.Subject ?? "").Trim();
        var text = (editor.Text ?? "").Trim();
        return SaveCoreAsync(subject, text, new(editor.Share && editor.IsNew, signedIn, communityId), saveLocal, send);
    }

    public static Task<HomeworkShareOutcome> SaveAsync(
        string subject, string text, HomeworkShareInput input, Func<string, string, Task> saveLocal, Func<string, string, Task> send)
    {
        ArgumentNullException.ThrowIfNull(saveLocal);
        ArgumentNullException.ThrowIfNull(send);
        return SaveCoreAsync(subject, text, input, saveLocal, send);
    }

    private static async Task<HomeworkShareOutcome> SaveCoreAsync(
        string subject, string text, HomeworkShareInput input, Func<string, string, Task> saveLocal, Func<string, string, Task> send)
    {
        ArgumentNullException.ThrowIfNull(input);
        await saveLocal(subject, text).ConfigureAwait(false);
        if (!input.Share) return new(true, false, "");
        if (!input.SignedIn) return new(true, false, SignInNote);
        if (string.IsNullOrWhiteSpace(input.CommunityId)) return new(true, false, LocalOnlyNote);
        try
        {
            await send(subject, text).ConfigureAwait(false);
            return new(true, true, SharedNote);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return new(true, false, FailedNote);
        }
    }

    public static GroupHomeworkBook Complete(
        IReadOnlyList<LocalHomeworkMark> local, IReadOnlyList<GroupCopyMark> copies, string actorId, string homeworkId, bool completed)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(copies);
        var actor = (actorId ?? "").Trim();
        var id = (homeworkId ?? "").Trim();
        return new(
            local.Select(row => row with { }).ToList(),
            copies.Select(row => row.HomeworkId == id && row.MemberId == actor ? row with { Completed = completed } : row with { }).ToList());
    }
}
